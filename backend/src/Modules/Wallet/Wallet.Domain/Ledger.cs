using TillKnowIT.SharedKernel;

namespace Wallet.Domain;

/// <summary>
/// The chart of accounts. Every account's balance is the sum of its entries; nothing is ever edited.
/// Wallet balances are what the partner holds for parents (positive). PartnerFloat is the mirror of everything the partner holds for us
/// and is therefore negative; it is what the daily reconciliation compares with the partner's own total.
/// </summary>
public enum LedgerAccountType { Wallet, PartnerFloat, ShopPayable, Fees, StaffAccount, SchoolCash }

public enum LedgerTransactionType { TopUp, CashTopUp, CashDeposit, Purchase, PurchaseReversal, Transfer, Refund, Payout, StaffSettlement }

public sealed record LedgerLeg(Guid AccountId, long AmountCents);

/// <summary>Account ids are worked out from what they belong to, so an account never needs looking up before it can be used.</summary>
public static class AccountIds
{
    public static Guid PartnerFloat { get; } = DeterministicGuid.From("wallet.account:partner_float");
    public static Guid Fees { get; } = DeterministicGuid.From("wallet.account:fees");
    public static Guid Wallet(Guid walletId) => DeterministicGuid.From($"wallet.account:wallet:{walletId}");
    public static Guid ShopPayable(Guid shopId) => DeterministicGuid.From($"wallet.account:shop_payable:{shopId}");
    public static Guid StaffAccount(Guid holderId) => DeterministicGuid.From($"wallet.account:staff:{holderId}");

    /// <summary>Cash a school office has taken for wallets and not yet paid in to the partner. Negative while the school owes it.</summary>
    public static Guid SchoolCash(Guid schoolId) => DeterministicGuid.From($"wallet.account:school_cash:{schoolId}");
}

public sealed class LedgerAccount : Entity
{
    private LedgerAccount() { }

    public LedgerAccount(Guid id, LedgerAccountType type, Guid? ownerId, DateTimeOffset now)
    {
        Id = id;
        Type = type;
        OwnerId = ownerId;
        CreatedAt = now;
    }

    public LedgerAccountType Type { get; private set; }

    /// <summary>The wallet, shop or holder the account belongs to. Empty for the system accounts.</summary>
    public Guid? OwnerId { get; private set; }
}

/// <summary>
/// One balanced movement of money: two or more entries that add up to exactly zero. Append only: a mistake is corrected by another
/// transaction, never by changing this one. The reference is unique, so repeating a request (a till retrying) can never move money twice.
/// </summary>
public sealed class LedgerTransaction : Entity
{
    private readonly List<LedgerEntry> _entries = [];

    private LedgerTransaction() { }

    public LedgerTransactionType Type { get; private set; }
    public string Reference { get; private set; } = default!;
    public string? PartnerRef { get; private set; }
    public string? Description { get; private set; }
    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public static Result<LedgerTransaction> Create(LedgerTransactionType type, string reference, IEnumerable<LedgerLeg> legs, DateTimeOffset now, string? partnerRef = null, string? description = null)
    {
        var list = legs.ToList();
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 100) return Result<LedgerTransaction>.Fail("ledger.reference_invalid", "A transaction needs a reference of up to 100 characters.");
        if (list.Count < 2) return Result<LedgerTransaction>.Fail("ledger.legs_required", "A transaction needs at least two entries.");
        if (list.Any(l => l.AmountCents == 0)) return Result<LedgerTransaction>.Fail("ledger.zero_entry", "An entry cannot be zero.");
        if (list.Sum(l => l.AmountCents) != 0) return Result<LedgerTransaction>.Fail("ledger.unbalanced", "The entries of a transaction must add up to zero.");

        var t = new LedgerTransaction { Type = type, Reference = reference.Trim(), PartnerRef = partnerRef, Description = description, CreatedAt = now };
        foreach (var l in list) t._entries.Add(new LedgerEntry(t.Id, l.AccountId, l.AmountCents, now));
        return Result<LedgerTransaction>.Ok(t);
    }
}

public sealed class LedgerEntry : Entity
{
    private LedgerEntry() { }

    internal LedgerEntry(Guid transactionId, Guid accountId, long amountCents, DateTimeOffset at)
    {
        TransactionId = transactionId;
        AccountId = accountId;
        AmountCents = amountCents;
        CreatedAt = at;
    }

    public Guid TransactionId { get; private set; }
    public Guid AccountId { get; private set; }
    public long AmountCents { get; private set; }
}

/// <summary>The moves of money, as balanced legs. Pure: no database, so the rules can be tested exhaustively.</summary>
public static class Postings
{
    public static List<LedgerLeg> TopUp(Guid walletId, long cents) =>
        [new(AccountIds.PartnerFloat, -cents), new(AccountIds.Wallet(walletId), cents)];

    /// <summary>W2-2: cash handed in at the school office. The wallet is credited at once; the school owes that cash until it pays it in.</summary>
    public static List<LedgerLeg> CashTopUp(Guid schoolId, Guid walletId, long cents) =>
        [new(AccountIds.SchoolCash(schoolId), -cents), new(AccountIds.Wallet(walletId), cents)];

    /// <summary>The school pays its collected cash in to the partner, which now holds it.</summary>
    public static List<LedgerLeg> CashDeposit(Guid schoolId, long cents) =>
        [new(AccountIds.PartnerFloat, -cents), new(AccountIds.SchoolCash(schoolId), cents)];

    /// <summary>The shop gets the sale less the platform fee; the fee goes to the fees account.</summary>
    public static List<LedgerLeg> Purchase(Guid walletId, Guid shopId, long cents, long feeCents)
    {
        var legs = new List<LedgerLeg> { new(AccountIds.Wallet(walletId), -cents), new(AccountIds.ShopPayable(shopId), cents - feeCents) };
        if (feeCents > 0) legs.Add(new(AccountIds.Fees, feeCents));
        return legs;
    }

    /// <summary>A purchase on a teacher's monthly staff account: nothing is taken from a wallet; the account owes the school.</summary>
    public static List<LedgerLeg> StaffPurchase(Guid holderId, Guid shopId, long cents, long feeCents)
    {
        var legs = new List<LedgerLeg> { new(AccountIds.StaffAccount(holderId), -cents), new(AccountIds.ShopPayable(shopId), cents - feeCents) };
        if (feeCents > 0) legs.Add(new(AccountIds.Fees, feeCents));
        return legs;
    }

    public static List<LedgerLeg> Reversal(Guid walletId, Guid shopId, long cents, long feeCents)
    {
        var legs = new List<LedgerLeg> { new(AccountIds.Wallet(walletId), cents), new(AccountIds.ShopPayable(shopId), -(cents - feeCents)) };
        if (feeCents > 0) legs.Add(new(AccountIds.Fees, -feeCents));
        return legs;
    }

    public static List<LedgerLeg> Transfer(Guid fromWalletId, Guid toWalletId, long cents) =>
        [new(AccountIds.Wallet(fromWalletId), -cents), new(AccountIds.Wallet(toWalletId), cents)];

    /// <summary>Money goes back to the parent through the partner.</summary>
    public static List<LedgerLeg> Refund(Guid walletId, long cents) =>
        [new(AccountIds.Wallet(walletId), -cents), new(AccountIds.PartnerFloat, cents)];

    public static List<LedgerLeg> Payout(Guid shopId, long cents) =>
        [new(AccountIds.ShopPayable(shopId), -cents), new(AccountIds.PartnerFloat, cents)];

    /// <summary>The school pays what its teachers owe: the staff account is cleared against money the partner now holds.</summary>
    public static List<LedgerLeg> StaffSettlement(Guid holderId, long cents) =>
        [new(AccountIds.PartnerFloat, -cents), new(AccountIds.StaffAccount(holderId), cents)];

    /// <summary>The platform fee on a sale, in basis points (150 = 1.5%), rounded half up. Never more than the sale.</summary>
    public static long Fee(long cents, int basisPoints) =>
        basisPoints <= 0 ? 0 : Math.Min(cents, (cents * basisPoints + 5_000) / 10_000);
}
