using System.Text.Json;
using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.Security;
using TillKnowIT.SharedKernel;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

/// <summary>
/// Posts a purchase to the ledger and keeps the record of what was bought. Used by online till payments, offline settlement and pre-orders,
/// so the money rules live in exactly one place. Repeating a purchase id never moves money twice.
/// </summary>
public sealed class PurchasePoster(IWalletStore store, TimeProvider clock)
{
    public sealed record Posted(WalletPurchase Purchase, bool Repeated, bool Overdrawn, long BalanceBefore, long BalanceAfter);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Reference(Guid purchaseId) => $"sale:{purchaseId:N}";

    public async Task<Result<Posted>> Post(
        School school, Holder holder, SchoolWallet wallet, Guid purchaseId, long amountCents, IReadOnlyCollection<SaleItem> items,
        Guid? deviceId, Guid? staffId, DateTimeOffset soldAt, bool offline, bool staffAccount, CancellationToken ct, bool saveNow = true)
    {
        var reference = Reference(purchaseId);
        if (await store.TransactionExists(reference, ct))
        {
            var earlier = await store.GetPurchase(purchaseId, ct);
            return earlier is not null && earlier.WalletId == wallet.Id
                ? Result<Posted>.Ok(new Posted(earlier, true, false, 0, 0))
                : Result<Posted>.Fail("sale.id_reused", "That sale number was already used for something else.");
        }

        var now = clock.GetUtcNow();
        var fee = Postings.Fee(amountCents, school.FeeBasisPoints);
        var legs = staffAccount ? Postings.StaffPurchase(holder.Id, school.ShopId, amountCents, fee) : Postings.Purchase(wallet.Id, school.ShopId, amountCents, fee);
        var txn = LedgerTransaction.Create(LedgerTransactionType.Purchase, reference, legs, now, description: "Tuckshop purchase");
        if (!txn.IsSuccess) return Result<Posted>.Fail(txn.Error!.Code, txn.Error.Message);

        var before = staffAccount ? 0 : await store.Balance(wallet.AccountId, ct);
        await store.Add(txn.Value!, ct);
        var itemsJson = JsonSerializer.Serialize(items, Json);
        var purchase = new WalletPurchase(purchaseId, txn.Value!.Id, wallet.Id, holder.Id, school.ShopId, deviceId, staffId, amountCents, fee, itemsJson, offline, soldAt, now);
        await store.Add(purchase, ct);
        if (!staffAccount) wallet.NoteDebit();

        if (saveNow)
        {
            try { await store.Save(ct); }
            catch (ConcurrencyConflictException) { return Result<Posted>.Fail("wallet.busy", "The wallet was being used at the same moment. Try again."); }
        }

        var after = staffAccount ? 0 : before - amountCents;
        return Result<Posted>.Ok(new Posted(purchase, false, !staffAccount && before < amountCents, before, after));
    }

    public static List<SaleItem> ToItems(IEnumerable<SaleItemDto>? dtos) =>
        (dtos ?? []).Select(i => new SaleItem(i.ProductId, i.CategoryCode, i.Name ?? "", Math.Max(1, i.Quantity), i.PriceCents)).ToList();
}

/// <summary>Messages after a purchase: the optional push to parents and the low balance alert. Never blocks or fails the sale.</summary>
public sealed class PurchaseAlerts(IWalletStore store, IWalletNotifier notifier)
{
    public async Task After(Holder holder, SchoolWallet wallet, long amountCents, long balanceBefore, long balanceAfter, Guid purchaseId, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            foreach (var g in await store.GuardiansOf(holder.Id, ct))
            {
                if (g.NotifyOnPurchase)
                    await notifier.ToParent(g.ParentPersonId, "wallet.purchase", new() { ["child"] = holder.FirstName, ["amount"] = Rands.Rand(amountCents), ["balance"] = Rands.Rand(balanceAfter) },
                        $"wallet:purchase:{purchaseId:N}:{g.ParentPersonId:N}", whatsapp: false, ct);

                // Told once, when the balance crosses the line, not on every sale after it.
                if (g.LowBalanceAlertCents is { } line && balanceBefore >= line && balanceAfter < line)
                    await notifier.ToParent(g.ParentPersonId, "wallet.low_balance", new() { ["child"] = holder.FirstName, ["balance"] = Rands.Rand(balanceAfter) },
                        $"wallet:low:{wallet.Id:N}:{BusinessDay.Of(now):yyyyMMdd}:{g.ParentPersonId:N}", whatsapp: true, ct);
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // A message that cannot be sent must never undo or delay a sale that has already happened.
        }
    }
}

internal static class Rands
{
    public static string Rand(long cents) => "R" + (cents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

// ---------------------------------------------------------------- W3-1: paying at the tuckshop

public sealed record ProcessWalletPayment(Guid ShopId, WalletPaymentRequest Request) : ICommand<WalletPaymentResponse>;

internal sealed class WalletPaymentHandler(IWalletStore store, ITillAccessChecker tills, PurchasePoster poster, PurchaseAlerts alerts, WalletKeys keys, TimeProvider clock)
    : ICommandHandler<ProcessWalletPayment, WalletPaymentResponse>
{
    public async Task<Result<WalletPaymentResponse>> Handle(ProcessWalletPayment c, CancellationToken ct)
    {
        var r = c.Request;
        var access = await tills.Check(c.ShopId, r.DeviceId, r.StaffId, "sale.create", ct);
        if (!access.IsSuccess) return Result<WalletPaymentResponse>.Fail(access.Error!.Code, access.Error.Message);

        var school = await store.SchoolByShop(c.ShopId, ct);
        if (school is null) return Result<WalletPaymentResponse>.Fail("wallet.no_school", "This shop does not take wallet payments.");
        if (!school.IsTrading) return Result<WalletPaymentResponse>.Fail("wallet.school_not_active", "This school is not active yet.");

        var now = clock.GetUtcNow();
        var who = await Resolve(school, r, now, ct);
        if (!who.IsSuccess) return Result<WalletPaymentResponse>.Fail(who.Error!.Code, who.Error.Message);
        var (holder, wallet) = who.Value;

        var items = PurchasePoster.ToItems(r.Items);
        var staffAccount = r.UseStaffAccount;
        if (staffAccount)
        {
            if (holder.Type != HolderType.Teacher) return Result<WalletPaymentResponse>.Fail("staff.not_a_teacher", "Only a teacher can use a staff account.");
            if (!school.StaffAccountsEnabled) return Result<WalletPaymentResponse>.Fail("staff.accounts_disabled", "This school does not use staff accounts.");
        }

        // A repeated sale id answers with what happened the first time, before any rule is looked at again.
        if (await store.TransactionExists(PurchasePoster.Reference(r.SaleId), ct))
            return await Respond(await poster.Post(school, holder, wallet, r.SaleId, r.AmountCents, items, r.DeviceId, r.StaffId, now, false, staffAccount, ct), holder, wallet, staffAccount, ct);

        var spent = await WalletAccess.SpentToday(store, wallet.Id, now, ct);
        var balance = staffAccount ? 0 : await store.Balance(wallet.AccountId, ct);
        var context = new SpendContext(holder.Status, wallet.Status, wallet.DailyLimitCents, school.DailyCeilingCents, spent, balance, await store.Restrictions(wallet.Id, ct));
        var allowed = SpendRules.Check(context, r.AmountCents, items, checkBalance: !staffAccount);
        if (!allowed.IsSuccess) return Result<WalletPaymentResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var posted = await poster.Post(school, holder, wallet, r.SaleId, r.AmountCents, items, r.DeviceId, r.StaffId, now, false, staffAccount, ct);
        var response = await Respond(posted, holder, wallet, staffAccount, ct);
        if (posted.IsSuccess && !posted.Value!.Repeated)
            await alerts.After(holder, wallet, r.AmountCents, posted.Value.BalanceBefore, posted.Value.BalanceAfter, r.SaleId, now, ct);
        return response;
    }

    private async Task<Result<WalletPaymentResponse>> Respond(Result<PurchasePoster.Posted> posted, Holder holder, SchoolWallet wallet, bool staffAccount, CancellationToken ct)
    {
        if (!posted.IsSuccess) return Result<WalletPaymentResponse>.Fail(posted.Error!.Code, posted.Error.Message);
        var p = posted.Value!.Purchase;
        long? balance = staffAccount ? null : await store.Balance(wallet.AccountId, ct);
        return Result<WalletPaymentResponse>.Ok(new WalletPaymentResponse(p.Id, holder.Id, holder.DisplayName, holder.Grade, holder.ClassName, holder.PhotoUrl is not null, p.AmountCents, balance, posted.Value.Repeated));
    }

    /// <summary>Finds who is paying from a card or a QR code. A person from another school is "unknown": nothing about them is revealed.</summary>
    private async Task<Result<(Holder, SchoolWallet)>> Resolve(School school, WalletPaymentRequest r, DateTimeOffset now, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(r.QrToken))
        {
            var subject = ExpiringToken.Verify(keys.QrKey, r.QrToken, now);
            if (subject is null || !subject.StartsWith("wallet:", StringComparison.Ordinal) || !Guid.TryParseExact(subject["wallet:".Length..], "N", out var walletId))
                return Result<(Holder, SchoolWallet)>.Fail("qr.invalid", "This code has expired or is not valid. Ask for a fresh one.");
            var wallet = await store.GetWallet(walletId, ct);
            if (wallet is null || wallet.SchoolId != school.Id) return Result<(Holder, SchoolWallet)>.Fail("qr.invalid", "This code has expired or is not valid. Ask for a fresh one.");
            return Result<(Holder, SchoolWallet)>.Ok(((await store.GetHolder(wallet.HolderId, ct))!, wallet));
        }

        if (string.IsNullOrWhiteSpace(r.Token)) return Result<(Holder, SchoolWallet)>.Fail("token.required", "Scan a QR code or tap a card.");
        var token = await store.TokenByHash(HolderToken.Hash(r.Token), ct);
        if (token is null) return Result<(Holder, SchoolWallet)>.Fail("token.unknown", "This card is not linked to anyone here.");
        var holder = (await store.GetHolder(token.HolderId, ct))!;
        if (holder.SchoolId != school.Id) return Result<(Holder, SchoolWallet)>.Fail("token.unknown", "This card is not linked to anyone here.");
        if (token.Status != TokenStatus.Active) return Result<(Holder, SchoolWallet)>.Fail("token.blocked", "This card has been blocked.");
        return Result<(Holder, SchoolWallet)>.Ok((holder, (await store.WalletOfHolder(holder.Id, ct))!));
    }
}

// ---------------------------------------------------------------- W3-1: the rotating QR code on a phone

public sealed record GetWalletQr(Guid WalletId, Guid CallerPersonId) : IQuery<QrResponse>;

internal sealed class QrHandler(IWalletStore store, WalletKeys keys, TimeProvider clock) : IQueryHandler<GetWalletQr, QrResponse>
{
    /// <summary>A QR code works for a minute, so a screenshot is useless almost at once.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public async Task<Result<QrResponse>> Handle(GetWalletQr q, CancellationToken ct)
    {
        var opened = await WalletAccess.Parent(store, q.WalletId, q.CallerPersonId, ct);
        if (!opened.IsSuccess) return Result<QrResponse>.Fail(opened.Error!.Code, opened.Error.Message);
        if (opened.Value.Wallet.Status != WalletStatus.Active) return Result<QrResponse>.Fail("wallet.frozen", "This wallet is not active.");
        var (token, expires) = ExpiringToken.Issue(keys.QrKey, $"wallet:{opened.Value.Wallet.Id:N}", clock.GetUtcNow(), Lifetime);
        return Result<QrResponse>.Ok(new QrResponse(token, expires));
    }
}
