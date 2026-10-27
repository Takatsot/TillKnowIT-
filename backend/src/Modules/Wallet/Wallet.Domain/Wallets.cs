using TillKnowIT.Security;
using TillKnowIT.SharedKernel;

namespace Wallet.Domain;

public enum WalletStatus { Active, Frozen, Closed }
public enum RestrictionKind { Product, Category }

/// <summary>
/// A wallet belongs to one holder. Its balance is never stored: it is the sum of its ledger entries. Every debit bumps the version, so two
/// spends at the same moment cannot both succeed against money that only covers one.
/// </summary>
public sealed class SchoolWallet : VersionedEntity
{
    public const long MaxDailyLimitCents = 500_000;

    private SchoolWallet() { }

    public Guid HolderId { get; private set; }
    public Guid SchoolId { get; private set; }
    public WalletStatus Status { get; private set; }

    /// <summary>A parent's own cap per day. Null means none.</summary>
    public long? DailyLimitCents { get; private set; }

    public Guid AccountId => AccountIds.Wallet(Id);

    public static SchoolWallet Open(Guid holderId, Guid schoolId, DateTimeOffset now) =>
        new() { HolderId = holderId, SchoolId = schoolId, Status = WalletStatus.Active, CreatedAt = now };

    public Result SetDailyLimit(long? cents)
    {
        if (cents is < 0 or > MaxDailyLimitCents) return Result.Fail("wallet.limit_invalid", "The daily limit is from R0 to R5000.");
        DailyLimitCents = cents;
        Touch();
        return Result.Ok();
    }

    public void Freeze() { Status = WalletStatus.Frozen; Touch(); }
    public void Unfreeze() { if (Status == WalletStatus.Frozen) { Status = WalletStatus.Active; Touch(); } }
    public void Close() { Status = WalletStatus.Closed; Touch(); }

    /// <summary>Call whenever money leaves this wallet; the version change is what makes a second simultaneous debit fail.</summary>
    public void NoteDebit() => Touch();

    /// <summary>Something the tills need to hear about changed (blocked items); the version moves so a sync sees it.</summary>
    public void NoteChange() => Touch();
}

/// <summary>W2-3: something a parent has blocked, by product or by category.</summary>
public sealed class WalletRestriction : Entity
{
    private WalletRestriction() { }

    public WalletRestriction(Guid walletId, RestrictionKind kind, string key, DateTimeOffset now)
    {
        WalletId = walletId;
        Kind = kind;
        Key = key.Trim().ToLowerInvariant();
        CreatedAt = now;
    }

    public Guid WalletId { get; private set; }
    public RestrictionKind Kind { get; private set; }
    public string Key { get; private set; } = default!;
}

/// <summary>What is being bought. Product and category come from the till's catalogue; the name is for messages.</summary>
public sealed record SaleItem(string? ProductId, string? CategoryCode, string Name, int Quantity, long PriceCents);

public sealed record SpendContext(
    HolderStatus HolderStatus, WalletStatus WalletStatus, long? WalletLimitCents, long? SchoolCeilingCents,
    long SpentTodayCents, long BalanceCents, IReadOnlyCollection<WalletRestriction> Restrictions);

public static class SpendRules
{
    public const long MaxPurchaseCents = 200_000;

    public static long? EffectiveLimit(long? walletLimit, long? schoolCeiling) =>
        (walletLimit, schoolCeiling) switch
        {
            (null, null) => null,
            (null, var c) => c,
            (var w, null) => w,
            (var w, var c) => Math.Min(w!.Value, c!.Value),
        };

    /// <summary>
    /// Whether this purchase may go ahead, and if not, the first reason, in the order a cashier would want to hear it.
    /// The offline path does not call this with a balance: it is bound by the signed allowance instead.
    /// </summary>
    public static Result Check(SpendContext c, long amountCents, IReadOnlyCollection<SaleItem> items, bool checkBalance = true)
    {
        if (c.HolderStatus != HolderStatus.Active) return Result.Fail("wallet.holder_inactive", "This learner is no longer at the school.");
        if (c.WalletStatus != WalletStatus.Active) return Result.Fail("wallet.frozen", "This wallet is not active.");
        if (amountCents <= 0 || amountCents > MaxPurchaseCents) return Result.Fail("wallet.amount_invalid", "The amount must be more than zero and at most R2000.");

        var blocked = items.FirstOrDefault(i => c.Restrictions.Any(r =>
            (r.Kind == RestrictionKind.Product && i.ProductId is not null && r.Key == i.ProductId.ToLowerInvariant()) ||
            (r.Kind == RestrictionKind.Category && i.CategoryCode is not null && r.Key == i.CategoryCode.ToLowerInvariant())));
        if (blocked is not null) return Result.Fail("wallet.item_blocked", $"{blocked.Name} is blocked on this wallet.");

        var limit = EffectiveLimit(c.WalletLimitCents, c.SchoolCeilingCents);
        if (limit is { } l && c.SpentTodayCents + amountCents > l) return Result.Fail("wallet.daily_limit", "This is over the daily limit.");
        if (checkBalance && c.BalanceCents < amountCents) return Result.Fail("wallet.insufficient", "Not enough money in the wallet.");
        return Result.Ok();
    }

    /// <summary>What a till may spend offline today: the balance, but never more than what is left of the day's limit.</summary>
    public static long Allowance(long balanceCents, long? limitCents, long spentTodayCents)
    {
        var room = limitCents is { } l ? Math.Max(0, l - spentTodayCents) : long.MaxValue;
        return Math.Max(0, Math.Min(balanceCents, room));
    }
}

/// <summary>W3-2: the server's signed promise of how much one wallet may spend on one day without a connection.</summary>
public sealed record SignedAllowance(Guid WalletId, DateOnly Day, long TotalCents, int Share, int Shares)
{
    /// <summary>What this one till may spend: an equal share of the total, rounded down so the tills together can never exceed it.</summary>
    public long PerTillCents => TotalCents / Math.Max(1, Shares);
}

public static class Allowances
{
    public static string Issue(string key, SignedAllowance a) =>
        SignedToken.Sign(key, $"allowance|{a.WalletId:N}|{a.Day:yyyy-MM-dd}|{a.TotalCents}|{a.Share}|{a.Shares}");

    public static SignedAllowance? Verify(string key, string? token)
    {
        var payload = SignedToken.Verify(key, token);
        var p = payload?.Split('|');
        if (p is not { Length: 6 } || p[0] != "allowance") return null;
        if (!Guid.TryParseExact(p[1], "N", out var wallet) || !DateOnly.TryParseExact(p[2], "yyyy-MM-dd", out var day)) return null;
        if (!long.TryParse(p[3], out var cents) || !int.TryParse(p[4], out var share) || !int.TryParse(p[5], out var shares)) return null;
        return cents >= 0 && shares >= 1 && share >= 0 && share < shares ? new SignedAllowance(wallet, day, cents, share, shares) : null;
    }
}

/// <summary>A wallet sale, kept for the parent's statement and the daily limit. The ledger holds the money; this holds what was bought.</summary>
public sealed class WalletPurchase : Entity
{
    private WalletPurchase() { }

    public WalletPurchase(Guid id, Guid transactionId, Guid walletId, Guid holderId, Guid shopId, Guid? deviceId, Guid? staffId, long amountCents, long feeCents, string itemsJson, bool offline, DateTimeOffset soldAt, DateTimeOffset now)
    {
        Id = id;
        TransactionId = transactionId;
        WalletId = walletId;
        HolderId = holderId;
        ShopId = shopId;
        DeviceId = deviceId;
        StaffId = staffId;
        AmountCents = amountCents;
        FeeCents = feeCents;
        ItemsJson = itemsJson;
        Offline = offline;
        SoldAt = soldAt;
        CreatedAt = now;
    }

    public Guid TransactionId { get; private set; }
    public Guid WalletId { get; private set; }
    public Guid HolderId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid? StaffId { get; private set; }
    public long AmountCents { get; private set; }

    /// <summary>What the platform kept of this sale, so a cancellation gives back exactly what was taken.</summary>
    public long FeeCents { get; private set; }
    public string ItemsJson { get; private set; } = "[]";
    public bool Offline { get; private set; }
    public DateTimeOffset SoldAt { get; private set; }
    public DateTimeOffset? ReversedAt { get; private set; }

    public void Reverse(DateTimeOffset now) => ReversedAt ??= now;
}

public enum OfflineSaleStatus { Settled, Rejected }

/// <summary>W3-2: the outcome of one offline sale when its till reached the server. Kept so a repeat upload is answered the same way.</summary>
public sealed class OfflineSaleRecord : Entity
{
    private OfflineSaleRecord() { }

    public OfflineSaleRecord(Guid saleId, Guid walletId, Guid deviceId, long amountCents, OfflineSaleStatus status, string? reason, bool overdrawn, DateTimeOffset soldAt, DateTimeOffset now)
    {
        Id = saleId;
        WalletId = walletId;
        DeviceId = deviceId;
        AmountCents = amountCents;
        Status = status;
        Reason = reason;
        Overdrawn = overdrawn;
        SoldAt = soldAt;
        CreatedAt = now;
    }

    public Guid WalletId { get; private set; }
    public long AmountCents { get; private set; }
    public OfflineSaleStatus Status { get; private set; }
    public string? Reason { get; private set; }

    /// <summary>The wallet no longer had the money by the time this settled, but the sale was inside the allowance, so the shop is paid and the wallet is overdrawn.</summary>
    public bool Overdrawn { get; private set; }

    public DateTimeOffset SoldAt { get; private set; }
}
