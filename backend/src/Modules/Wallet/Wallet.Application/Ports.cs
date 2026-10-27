using Identity.Contracts;
using TillKnowIT.SharedKernel;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

/// <summary>A ledger entry as the statement needs it: when, what kind of transaction, its reference and the signed amount on one account.</summary>
public sealed record LedgerEntryRow(Guid TransactionId, LedgerTransactionType Type, string Reference, string? Description, long AmountCents, DateTimeOffset At);

/// <summary>
/// Everything the wallet module keeps. One interface so one fake stands in for the database in tests. Stores must turn the database's
/// concurrency error into <see cref="ConcurrencyConflictException"/> when saving.
/// </summary>
public interface IWalletStore
{
    // schools
    Task Add(School school, CancellationToken ct);
    Task<School?> GetSchool(Guid schoolId, CancellationToken ct);
    Task<School?> SchoolByShop(Guid shopId, CancellationToken ct);
    Task<List<School>> AllSchools(CancellationToken ct);
    Task Add(SchoolAdmin admin, CancellationToken ct);
    Task<bool> IsSchoolAdmin(Guid schoolId, Guid personId, CancellationToken ct);

    // holders, wallets, tokens, guardians
    Task Add(Holder holder, CancellationToken ct);
    Task<Holder?> GetHolder(Guid holderId, CancellationToken ct);
    Task<List<Holder>> HoldersOf(Guid schoolId, CancellationToken ct);
    Task<Holder?> HolderByLinkCode(string linkCodeHash, CancellationToken ct);
    Task Add(SchoolWallet wallet, CancellationToken ct);
    Task<SchoolWallet?> GetWallet(Guid walletId, CancellationToken ct);
    Task<SchoolWallet?> WalletOfHolder(Guid holderId, CancellationToken ct);
    Task<List<SchoolWallet>> WalletsOf(Guid schoolId, CancellationToken ct);
    Task<List<WalletRestriction>> Restrictions(Guid walletId, CancellationToken ct);
    Task<List<WalletRestriction>> RestrictionsOfSchool(Guid schoolId, CancellationToken ct);
    Task ReplaceRestrictions(Guid walletId, List<WalletRestriction> restrictions, CancellationToken ct);
    Task Add(HolderToken token, CancellationToken ct);
    Task<HolderToken?> TokenByHash(string hash, CancellationToken ct);
    Task<HolderToken?> GetToken(Guid tokenId, CancellationToken ct);
    Task<List<HolderToken>> TokensOf(Guid holderId, CancellationToken ct);
    Task<List<HolderToken>> TokensOfSchool(Guid schoolId, CancellationToken ct);
    Task Add(Guardianship guardianship, CancellationToken ct);
    Task<Guardianship?> GuardianshipOf(Guid parentPersonId, Guid holderId, CancellationToken ct);
    Task<List<Guardianship>> GuardiansOf(Guid holderId, CancellationToken ct);
    Task<List<Guardianship>> GuardianshipsOfParent(Guid parentPersonId, CancellationToken ct);
    Task RemoveGuardianships(Guid holderId, CancellationToken ct);

    // ledger
    Task Add(LedgerAccount account, CancellationToken ct);
    Task<bool> AccountExists(Guid accountId, CancellationToken ct);
    Task Add(LedgerTransaction transaction, CancellationToken ct);
    Task<bool> TransactionExists(string reference, CancellationToken ct);
    Task<long> Balance(Guid accountId, CancellationToken ct);
    Task<Dictionary<Guid, long>> Balances(IReadOnlyCollection<Guid> accountIds, CancellationToken ct);
    Task<List<LedgerEntryRow>> EntriesOf(Guid accountId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);

    /// <summary>Transactions whose entries do not add up to zero. There should never be any: this is the check that proves it.</summary>
    Task<List<Guid>> UnbalancedTransactions(int take, CancellationToken ct);

    // purchases and offline sales
    Task Add(WalletPurchase purchase, CancellationToken ct);
    Task<WalletPurchase?> GetPurchase(Guid purchaseId, CancellationToken ct);
    Task<long> SpentBetween(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<Dictionary<Guid, long>> SpentByWallet(Guid schoolId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<List<WalletPurchase>> PurchasesOf(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<List<WalletPurchase>> PurchasesOfShop(Guid shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task Add(OfflineSaleRecord record, CancellationToken ct);
    Task<OfflineSaleRecord?> GetOfflineRecord(Guid saleId, CancellationToken ct);
    Task<long> OfflineSettledBetween(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    // top-ups
    Task Add(TopUp topUp, CancellationToken ct);
    Task<TopUp?> TopUpByPartnerRef(string partnerRef, CancellationToken ct);
    Task<bool> HasReceipt(string partner, string eventId, CancellationToken ct);
    Task Add(WalletWebhookReceipt receipt, CancellationToken ct);

    // menu and pre-orders
    Task<List<BreakSlot>> Breaks(Guid schoolId, CancellationToken ct);
    Task ReplaceBreaks(Guid schoolId, List<BreakSlot> breaks, CancellationToken ct);
    Task<List<MenuItem>> MenuOf(Guid schoolId, CancellationToken ct);
    Task<MenuItem?> GetMenuItem(Guid itemId, CancellationToken ct);
    Task Add(MenuItem item, CancellationToken ct);
    Task Add(PreOrder order, CancellationToken ct);
    Task<PreOrder?> GetPreOrder(Guid orderId, CancellationToken ct);
    Task<List<PreOrder>> PreOrdersOf(Guid schoolId, DateOnly collectOn, CancellationToken ct);
    Task<List<PreOrder>> PreOrdersOfHolder(Guid holderId, DateOnly from, CancellationToken ct);

    // refunds, payouts, reconciliation, erasure
    Task Add(RefundRequest request, CancellationToken ct);
    Task<RefundRequest?> GetRefund(Guid refundId, CancellationToken ct);
    Task<List<RefundRequest>> RefundsOf(Guid walletId, CancellationToken ct);
    Task Add(ShopPayout payout, CancellationToken ct);
    Task<List<ShopPayout>> PayoutsOf(Guid shopId, CancellationToken ct);
    Task Add(WalletReconciliation reconciliation, CancellationToken ct);
    Task<WalletReconciliation?> GetReconciliation(Guid id, CancellationToken ct);
    Task<WalletReconciliation?> OpenMismatch(CancellationToken ct);
    Task<bool> HasReconciliation(DateOnly day, CancellationToken ct);
    Task Add(ErasureRequest request, CancellationToken ct);
    Task<ErasureRequest?> ErasureOf(Guid holderId, CancellationToken ct);
    Task<List<ErasureRequest>> PendingErasures(CancellationToken ct);

    Task Save(CancellationToken ct);
}

public sealed record TopUpCheckoutRequest(string Reference, long AmountCents, string Description, string? ReturnUrl);
public sealed record TopUpCheckout(string PartnerRef, string Url);

/// <summary>
/// The licensed payment partner that holds parents' money. Card details never reach us. A real partner is connected later behind this
/// port; until then only a development fake exists and the real one fails with a clear message.
/// </summary>
public interface IStoredValuePartner
{
    Task<Result<TopUpCheckout>> CreateTopUpCheckout(TopUpCheckoutRequest request, CancellationToken ct);

    /// <summary>Money back to the parent who loaded it (an unused balance when a child leaves).</summary>
    Task<Result<string>> RefundToParent(Guid walletId, long amountCents, string reference, CancellationToken ct);

    /// <summary>The reference is an idempotency key at the partner: asking twice with the same one pays once.</summary>
    Task<Result<string>> PayoutToShop(Guid shopId, long amountCents, string reference, CancellationToken ct);

    /// <summary>Everything the partner holds for the platform right now, in cents. The ledger is reconciled against this.</summary>
    Task<long> HeldTotal(CancellationToken ct);
}

public interface IWalletWebhookVerifier
{
    bool Verify(string partner, string? signatureHeader, string body, DateTimeOffset now);
}

/// <summary>The keys that sign what leaves the server: rotating QR codes and daily allowances. Kept apart so one leaking does not expose the other.</summary>
public sealed record WalletKeys(string QrKey, string AllowanceKey);

public interface IWalletNotifier
{
    /// <summary>A push message (and WhatsApp if asked) to a parent. Never throws and never blocks the sale: a failed message is only logged.</summary>
    Task ToParent(Guid parentPersonId, string template, Dictionary<string, string> values, string dedupeKey, bool whatsapp, CancellationToken ct);

    Task ToShopOwner(Guid shopId, string template, Dictionary<string, string> values, string dedupeKey, CancellationToken ct);
}

internal static class SchoolAccess
{
    /// <summary>The caller is an admin of this school, or a platform admin.</summary>
    public static async Task<Result> Admin(IWalletStore store, IPermissionChecker permissions, Guid schoolId, Guid person, CancellationToken ct) =>
        await store.IsSchoolAdmin(schoolId, person, ct) || await permissions.Has(person, "school.manage", ct)
            ? Result.Ok()
            : Result.Fail("permission.denied", "You do not have access to this school.");

    public static async Task<Result> Platform(IPermissionChecker permissions, Guid person, string permission, CancellationToken ct) =>
        await permissions.Has(person, permission, ct) ? Result.Ok() : Result.Fail("permission.denied", "Only a platform admin can do this.");
}
