using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using TillKnowIT.Security;
using TillKnowIT.SharedKernel;
using TillKnowIT.Storage;
using Wallet.Application;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application.Tests;

/// <summary>The database, in memory. It enforces the same unique references the real one does and can simulate a concurrent change.</summary>
internal sealed class FakeWalletStore : IWalletStore
{
    public List<School> Schools { get; } = [];
    public List<SchoolAdmin> Admins { get; } = [];
    public List<Holder> Holders { get; } = [];
    public List<SchoolWallet> Wallets { get; } = [];
    public List<WalletRestriction> RestrictionRows { get; } = [];
    public List<HolderToken> Tokens { get; } = [];
    public List<Guardianship> Guardianships { get; } = [];
    public List<LedgerAccount> Accounts { get; } = [];
    public List<LedgerTransaction> Transactions { get; } = [];
    public List<WalletPurchase> Purchases { get; } = [];
    public List<OfflineSaleRecord> OfflineRecords { get; } = [];
    private readonly List<WalletPurchase> _pendingPurchases = [];
    private readonly List<OfflineSaleRecord> _pendingOffline = [];
    public List<TopUp> TopUps { get; } = [];
    public List<WalletWebhookReceipt> Receipts { get; } = [];
    public List<BreakSlot> BreakRows { get; } = [];
    public List<MenuItem> MenuRows { get; } = [];
    public List<PreOrder> PreOrderRows { get; } = [];
    public List<RefundRequest> Refunds { get; } = [];
    public List<ShopPayout> Payouts { get; } = [];
    public List<WalletReconciliation> Reconciliations { get; } = [];
    public List<ErasureRequest> Erasures { get; } = [];

    /// <summary>When set, the next Save throws what a real database would on a concurrent change, and nothing is kept.</summary>
    public bool ConflictOnNextSave { get; set; }

    public int Saves { get; private set; }

    private readonly List<LedgerTransaction> _pendingTransactions = [];

    public Task Add(School s, CancellationToken ct) { Schools.Add(s); return Task.CompletedTask; }
    public Task<School?> GetSchool(Guid id, CancellationToken ct) => Task.FromResult(Schools.FirstOrDefault(s => s.Id == id));
    public Task<School?> SchoolByShop(Guid shopId, CancellationToken ct) => Task.FromResult(Schools.FirstOrDefault(s => s.ShopId == shopId));
    public Task<List<School>> AllSchools(CancellationToken ct) => Task.FromResult(Schools.ToList());
    public Task Add(SchoolAdmin a, CancellationToken ct) { Admins.Add(a); return Task.CompletedTask; }
    public Task<bool> IsSchoolAdmin(Guid schoolId, Guid personId, CancellationToken ct) => Task.FromResult(Admins.Any(a => a.SchoolId == schoolId && a.PersonId == personId));

    public Task Add(Holder h, CancellationToken ct) { Holders.Add(h); return Task.CompletedTask; }
    public Task<Holder?> GetHolder(Guid id, CancellationToken ct) => Task.FromResult(Holders.FirstOrDefault(h => h.Id == id));
    public Task<List<Holder>> HoldersOf(Guid schoolId, CancellationToken ct) => Task.FromResult(Holders.Where(h => h.SchoolId == schoolId).ToList());
    public Task<Holder?> HolderByLinkCode(string hash, CancellationToken ct) => Task.FromResult(Holders.FirstOrDefault(h => h.LinkCodeHash == hash));
    public Task Add(SchoolWallet w, CancellationToken ct) { Wallets.Add(w); return Task.CompletedTask; }
    public Task<SchoolWallet?> GetWallet(Guid id, CancellationToken ct) => Task.FromResult(Wallets.FirstOrDefault(w => w.Id == id));
    public Task<SchoolWallet?> WalletOfHolder(Guid holderId, CancellationToken ct) => Task.FromResult(Wallets.FirstOrDefault(w => w.HolderId == holderId));
    public Task<List<SchoolWallet>> WalletsOf(Guid schoolId, CancellationToken ct) => Task.FromResult(Wallets.Where(w => w.SchoolId == schoolId).ToList());
    public Task<List<WalletRestriction>> Restrictions(Guid walletId, CancellationToken ct) => Task.FromResult(RestrictionRows.Where(r => r.WalletId == walletId).ToList());
    public Task<List<WalletRestriction>> RestrictionsOfSchool(Guid schoolId, CancellationToken ct)
    {
        var ids = Wallets.Where(w => w.SchoolId == schoolId).Select(w => w.Id).ToHashSet();
        return Task.FromResult(RestrictionRows.Where(r => ids.Contains(r.WalletId)).ToList());
    }
    public Task ReplaceRestrictions(Guid walletId, List<WalletRestriction> list, CancellationToken ct)
    {
        RestrictionRows.RemoveAll(r => r.WalletId == walletId);
        RestrictionRows.AddRange(list);
        return Task.CompletedTask;
    }
    public Task Add(HolderToken t, CancellationToken ct)
    {
        if (Tokens.Any(x => x.TokenHash == t.TokenHash)) throw new InvalidOperationException("unique token hash");
        Tokens.Add(t);
        return Task.CompletedTask;
    }
    public Task<HolderToken?> TokenByHash(string hash, CancellationToken ct) => Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == hash));
    public Task<HolderToken?> GetToken(Guid id, CancellationToken ct) => Task.FromResult(Tokens.FirstOrDefault(t => t.Id == id));
    public Task<List<HolderToken>> TokensOf(Guid holderId, CancellationToken ct) => Task.FromResult(Tokens.Where(t => t.HolderId == holderId).ToList());
    public Task<List<HolderToken>> TokensOfSchool(Guid schoolId, CancellationToken ct)
    {
        var ids = Holders.Where(h => h.SchoolId == schoolId).Select(h => h.Id).ToHashSet();
        return Task.FromResult(Tokens.Where(t => ids.Contains(t.HolderId)).ToList());
    }
    public Task Add(Guardianship g, CancellationToken ct) { Guardianships.Add(g); return Task.CompletedTask; }
    public Task<Guardianship?> GuardianshipOf(Guid parent, Guid holder, CancellationToken ct) => Task.FromResult(Guardianships.FirstOrDefault(g => g.ParentPersonId == parent && g.HolderId == holder));
    public Task<List<Guardianship>> GuardiansOf(Guid holder, CancellationToken ct) => Task.FromResult(Guardianships.Where(g => g.HolderId == holder).ToList());
    public Task<List<Guardianship>> GuardianshipsOfParent(Guid parent, CancellationToken ct) => Task.FromResult(Guardianships.Where(g => g.ParentPersonId == parent).ToList());
    public Task RemoveGuardianships(Guid holder, CancellationToken ct) { Guardianships.RemoveAll(g => g.HolderId == holder); return Task.CompletedTask; }

    public Task Add(LedgerAccount a, CancellationToken ct)
    {
        if (Accounts.Any(x => x.Id == a.Id)) throw new InvalidOperationException("duplicate account");
        Accounts.Add(a);
        return Task.CompletedTask;
    }
    public Task<bool> AccountExists(Guid id, CancellationToken ct) => Task.FromResult(Accounts.Any(a => a.Id == id));
    public Task Add(LedgerTransaction t, CancellationToken ct)
    {
        if (Transactions.Concat(_pendingTransactions).Any(x => x.Reference == t.Reference)) throw new InvalidOperationException($"unique reference {t.Reference}");
        _pendingTransactions.Add(t);
        return Task.CompletedTask;
    }
    public Task<bool> TransactionExists(string reference, CancellationToken ct) => Task.FromResult(Transactions.Concat(_pendingTransactions).Any(t => t.Reference == reference));
    public Task<long> Balance(Guid accountId, CancellationToken ct) => Task.FromResult(Transactions.SelectMany(t => t.Entries).Where(e => e.AccountId == accountId).Sum(e => e.AmountCents));
    public Task<Dictionary<Guid, long>> Balances(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult(Transactions.SelectMany(t => t.Entries).Where(e => ids.Contains(e.AccountId)).GroupBy(e => e.AccountId).ToDictionary(g => g.Key, g => g.Sum(e => e.AmountCents)));
    public Task<List<LedgerEntryRow>> EntriesOf(Guid accountId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        Task.FromResult(Transactions.SelectMany(t => t.Entries.Where(e => e.AccountId == accountId).Select(e => new LedgerEntryRow(t.Id, t.Type, t.Reference, t.Description, e.AmountCents, t.CreatedAt)))
            .Where(e => (from is null || e.At >= from) && (to is null || e.At < to)).ToList());
    public Task<List<Guid>> UnbalancedTransactions(int take, CancellationToken ct) =>
        Task.FromResult(Transactions.Where(t => t.Entries.Sum(e => e.AmountCents) != 0).Select(t => t.Id).Take(take).ToList());

    public Task Add(WalletPurchase p, CancellationToken ct) { _pendingPurchases.Add(p); return Task.CompletedTask; }
    public Task<WalletPurchase?> GetPurchase(Guid id, CancellationToken ct) => Task.FromResult(Purchases.FirstOrDefault(p => p.Id == id));
    public Task<long> SpentBetween(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult(Purchases.Where(p => p.WalletId == walletId && p.ReversedAt is null && p.SoldAt >= from && p.SoldAt < to).Sum(p => p.AmountCents));
    public Task<Dictionary<Guid, long>> SpentByWallet(Guid schoolId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var ids = Wallets.Where(w => w.SchoolId == schoolId).Select(w => w.Id).ToHashSet();
        return Task.FromResult(Purchases.Where(p => ids.Contains(p.WalletId) && p.ReversedAt is null && p.SoldAt >= from && p.SoldAt < to).GroupBy(p => p.WalletId).ToDictionary(g => g.Key, g => g.Sum(p => p.AmountCents)));
    }
    public Task<List<WalletPurchase>> PurchasesOf(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult(Purchases.Where(p => p.WalletId == walletId && p.SoldAt >= from && p.SoldAt < to).ToList());
    public Task<List<WalletPurchase>> PurchasesOfShop(Guid shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult(Purchases.Where(p => p.ShopId == shopId && p.SoldAt >= from && p.SoldAt < to).ToList());
    public Task Add(OfflineSaleRecord r, CancellationToken ct) { _pendingOffline.Add(r); return Task.CompletedTask; }
    public Task<OfflineSaleRecord?> GetOfflineRecord(Guid saleId, CancellationToken ct) => Task.FromResult(OfflineRecords.FirstOrDefault(r => r.Id == saleId));
    public Task<long> OfflineSettledBetween(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult(OfflineRecords.Where(r => r.WalletId == walletId && r.Status == OfflineSaleStatus.Settled && r.SoldAt >= from && r.SoldAt < to).Sum(r => r.AmountCents));

    public Task Add(TopUp t, CancellationToken ct) { TopUps.Add(t); return Task.CompletedTask; }
    public Task<TopUp?> TopUpByPartnerRef(string r, CancellationToken ct) => Task.FromResult(TopUps.FirstOrDefault(t => t.PartnerRef == r));
    public Task<bool> HasReceipt(string partner, string id, CancellationToken ct) => Task.FromResult(Receipts.Any(r => r.Partner == partner && r.EventId == id));
    public Task Add(WalletWebhookReceipt r, CancellationToken ct) { Receipts.Add(r); return Task.CompletedTask; }

    public Task<List<BreakSlot>> Breaks(Guid schoolId, CancellationToken ct) => Task.FromResult(BreakRows.Where(b => b.SchoolId == schoolId).ToList());
    public Task ReplaceBreaks(Guid schoolId, List<BreakSlot> list, CancellationToken ct)
    {
        BreakRows.RemoveAll(b => b.SchoolId == schoolId);
        BreakRows.AddRange(list);
        return Task.CompletedTask;
    }
    public Task<List<MenuItem>> MenuOf(Guid schoolId, CancellationToken ct) => Task.FromResult(MenuRows.Where(m => m.SchoolId == schoolId).ToList());
    public Task<MenuItem?> GetMenuItem(Guid id, CancellationToken ct) => Task.FromResult(MenuRows.FirstOrDefault(m => m.Id == id));
    public Task Add(MenuItem m, CancellationToken ct) { MenuRows.Add(m); return Task.CompletedTask; }
    public Task Add(PreOrder o, CancellationToken ct) { PreOrderRows.Add(o); return Task.CompletedTask; }
    public Task<PreOrder?> GetPreOrder(Guid id, CancellationToken ct) => Task.FromResult(PreOrderRows.FirstOrDefault(o => o.Id == id));
    public Task<List<PreOrder>> PreOrdersOf(Guid schoolId, DateOnly day, CancellationToken ct) => Task.FromResult(PreOrderRows.Where(o => o.SchoolId == schoolId && o.CollectOn == day).ToList());
    public Task<List<PreOrder>> PreOrdersOfHolder(Guid holderId, DateOnly from, CancellationToken ct) => Task.FromResult(PreOrderRows.Where(o => o.HolderId == holderId && o.CollectOn >= from).ToList());

    public Task Add(RefundRequest r, CancellationToken ct) { Refunds.Add(r); return Task.CompletedTask; }
    public Task<RefundRequest?> GetRefund(Guid id, CancellationToken ct) => Task.FromResult(Refunds.FirstOrDefault(r => r.Id == id));
    public Task<List<RefundRequest>> RefundsOf(Guid walletId, CancellationToken ct) => Task.FromResult(Refunds.Where(r => r.WalletId == walletId).ToList());
    public Task Add(ShopPayout p, CancellationToken ct) { Payouts.Add(p); return Task.CompletedTask; }
    public Task<List<ShopPayout>> PayoutsOf(Guid shopId, CancellationToken ct) => Task.FromResult(Payouts.Where(p => p.ShopId == shopId).ToList());
    public Task Add(WalletReconciliation r, CancellationToken ct) { Reconciliations.Add(r); return Task.CompletedTask; }
    public Task<WalletReconciliation?> GetReconciliation(Guid id, CancellationToken ct) => Task.FromResult(Reconciliations.FirstOrDefault(r => r.Id == id));
    public Task<WalletReconciliation?> OpenMismatch(CancellationToken ct) => Task.FromResult(Reconciliations.FirstOrDefault(r => r.Status == ReconciliationStatus.Mismatch));
    public Task<bool> HasReconciliation(DateOnly day, CancellationToken ct) => Task.FromResult(Reconciliations.Any(r => r.Day == day));
    public Task Add(ErasureRequest r, CancellationToken ct) { Erasures.Add(r); return Task.CompletedTask; }
    public Task<ErasureRequest?> ErasureOf(Guid holderId, CancellationToken ct) => Task.FromResult(Erasures.FirstOrDefault(e => e.HolderId == holderId));
    public Task<List<ErasureRequest>> PendingErasures(CancellationToken ct) => Task.FromResult(Erasures.Where(e => e.CompletedAt is null).ToList());

    public Task Save(CancellationToken ct)
    {
        if (ConflictOnNextSave)
        {
            ConflictOnNextSave = false;
            _pendingTransactions.Clear();   // nothing is kept
            _pendingPurchases.Clear();
            _pendingOffline.Clear();
            throw new ConcurrencyConflictException("simulated");
        }
        Saves++;
        Transactions.AddRange(_pendingTransactions);
        _pendingTransactions.Clear();
        Purchases.AddRange(_pendingPurchases);
        _pendingPurchases.Clear();
        OfflineRecords.AddRange(_pendingOffline);
        _pendingOffline.Clear();
        return Task.CompletedTask;
    }
}

internal sealed class FakePartner : IStoredValuePartner
{
    public bool Down { get; set; }
    public long Held { get; set; }
    public List<(Guid Wallet, long Cents, string Reference)> Refunds { get; } = [];
    public List<(Guid Shop, long Cents, string Reference)> ShopPayouts { get; } = [];
    public int Checkouts { get; private set; }

    public Task<Result<TopUpCheckout>> CreateTopUpCheckout(TopUpCheckoutRequest r, CancellationToken ct)
    {
        if (Down) return Task.FromResult(Result<TopUpCheckout>.Fail("payment.partner_down", "The partner is down."));
        Checkouts++;
        return Task.FromResult(Result<TopUpCheckout>.Ok(new TopUpCheckout($"ref_{Checkouts}", $"https://pay/{Checkouts}")));
    }

    public Task<Result<string>> RefundToParent(Guid walletId, long cents, string reference, CancellationToken ct)
    {
        if (Down) return Task.FromResult(Result<string>.Fail("payment.partner_down", "The partner is down."));
        Refunds.Add((walletId, cents, reference));
        return Task.FromResult(Result<string>.Ok("pr_" + reference));
    }

    public Task<Result<string>> PayoutToShop(Guid shopId, long cents, string reference, CancellationToken ct)
    {
        if (Down) return Task.FromResult(Result<string>.Fail("payment.partner_down", "The partner is down."));
        ShopPayouts.Add((shopId, cents, reference));
        return Task.FromResult(Result<string>.Ok("pp_" + reference));
    }

    public Task<long> HeldTotal(CancellationToken ct) => Task.FromResult(Held);
}

internal sealed class FakeNotifier : IWalletNotifier
{
    public List<(Guid Parent, string Template, Dictionary<string, string> Values, bool Whatsapp)> ToParents { get; } = [];
    public List<(Guid Shop, string Template, Dictionary<string, string> Values)> ToShops { get; } = [];
    public bool Throws { get; set; }

    public Task ToParent(Guid parent, string template, Dictionary<string, string> values, string dedupeKey, bool whatsapp, CancellationToken ct)
    {
        if (Throws) throw new InvalidOperationException("messaging is down");
        ToParents.Add((parent, template, values, whatsapp));
        return Task.CompletedTask;
    }

    public Task ToShopOwner(Guid shopId, string template, Dictionary<string, string> values, string dedupeKey, CancellationToken ct)
    {
        ToShops.Add((shopId, template, values));
        return Task.CompletedTask;
    }
}

internal sealed class FakeIdentity : IShopDirectory, IPersonDirectory, IPermissionChecker, ITillAccessChecker
{
    public Dictionary<Guid, string> VerifiedShops { get; } = [];
    public HashSet<Guid> People { get; } = [];
    public HashSet<Guid> PlatformAdmins { get; } = [];
    public HashSet<(Guid Shop, Guid Device, Guid Staff)> Tills { get; } = [];

    public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) =>
        Task.FromResult<ShopInfo?>(VerifiedShops.TryGetValue(shopId, out var status) ? new ShopInfo(shopId, "Tuckshop", status, Guid.NewGuid(), "0821110000", null, "en", "jhb") : null);
    public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(VerifiedShops.Keys.ToList());

    Task<PersonContact?> IPersonDirectory.Get(Guid personId, CancellationToken ct) =>
        Task.FromResult<PersonContact?>(People.Contains(personId) ? new PersonContact(personId, "Parent", "0821230000", null, "en") : null);

    public Task<bool> Has(Guid personId, string permission, CancellationToken ct) => Task.FromResult(PlatformAdmins.Contains(personId));

    public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct) =>
        Task.FromResult(Tills.Contains((shopId, deviceId, staffId)) ? Result.Ok() : Result.Fail("device.not_approved", "This till is not approved."));
}

internal sealed class FakeOrgAccess : IOrganisationAccess
{
    public HashSet<(Guid Person, Guid Org)> Granted { get; } = [];
    public Task<bool> Has(Guid personId, Guid organisationId, string permission, CancellationToken ct) => Task.FromResult(Granted.Contains((personId, organisationId)));
}

internal sealed class MemoryFileStore : IFileStore
{
    public Dictionary<string, StoredFile> Files { get; } = [];
    public Task<string> Save(string container, string name, string type, byte[] content, CancellationToken ct)
    {
        var url = $"tkfile://{container}/{Guid.NewGuid():N}";
        Files[url] = new StoredFile(content, type, name);
        return Task.FromResult(url);
    }
    public Task<StoredFile?> Open(string url, CancellationToken ct) => Task.FromResult(Files.GetValueOrDefault(url));
    public Task Delete(string url, CancellationToken ct) { Files.Remove(url); return Task.CompletedTask; }
}

internal sealed class AcceptAllWebhooks : IWalletWebhookVerifier
{
    public const string Secret = "wallet-webhook-secret";
    public bool Verify(string partner, string? header, string body, DateTimeOffset now) => WebhookSignature.Verify(Secret, header, body, now);
}

/// <summary>A school, its tuckshop, a platform admin, a school admin and a till, all set up, with every handler ready to call.</summary>
internal sealed class WalletWorld
{
    public static readonly DateTimeOffset Start = new(2026, 10, 26, 7, 0, 0, TimeSpan.Zero);   // Monday 09:00 in South Africa
    public const string QrKey = "qr-key-for-tests-0123456789";
    public const string AllowanceKey = "allowance-key-for-tests-0123456789";

    public FakeTimeProvider Clock { get; } = new(Start);
    public FakeWalletStore Store { get; } = new();
    public FakePartner Partner { get; } = new();
    public FakeNotifier Notifier { get; } = new();
    public FakeIdentity Identity { get; } = new();
    public FakeOrgAccess OrgAccess { get; } = new();
    public MemoryFileStore Files { get; } = new();
    public WalletKeys Keys { get; } = new(QrKey, AllowanceKey);

    public Guid PlatformAdmin { get; } = Guid.NewGuid();
    public Guid SchoolAdmin { get; } = Guid.NewGuid();
    public Guid ShopId { get; } = Guid.NewGuid();
    public Guid ShopOwner { get; } = Guid.NewGuid();
    public Guid DeviceId { get; } = Guid.NewGuid();
    public Guid DeviceId2 { get; } = Guid.NewGuid();
    public Guid StaffId { get; } = Guid.NewGuid();
    public School School { get; private set; } = default!;

    public WalletWorld()
    {
        Identity.VerifiedShops[ShopId] = "verified";
        Identity.People.Add(SchoolAdmin);
        Identity.PlatformAdmins.Add(PlatformAdmin);
        Identity.Tills.Add((ShopId, DeviceId, StaffId));
        Identity.Tills.Add((ShopId, DeviceId2, StaffId));
        OrgAccess.Granted.Add((ShopOwner, ShopId));
    }

    // ---- handler factories ----
    public SchoolHandlers Schools => new(Store, Identity, Identity, Identity, Clock);
    public HolderHandlers HolderOps => new(Store, Identity, Clock);
    public ImportHoldersHandler Importer => new(Store, Identity, Clock);
    public TokenHandlers TokenOps => new(Store, Identity, Clock);
    public PhotoHandlers Photos => new(Store, Files, Identity, Identity);
    public GuardianHandlers Guardians => new(Store, Identity, Clock);
    public WalletHandlers Wallets => new(Store, Identity, Clock);
    public StartTopUpHandler TopUpStarter => new(Store, Partner, Clock);
    public WalletWebhookHandler Webhooks => new(new AcceptAllWebhooks(), Store, Notifier, Clock);
    public CashHandlers Cash => new(Store, Identity, Clock);
    public TransferHandler Transfers => new(Store, Clock);
    public PurchasePoster Poster => new(Store, Clock);
    public PurchaseAlerts Alerts => new(Store, Notifier);
    public WalletPaymentHandler Payments => new(Store, Identity, Poster, Alerts, Keys, Clock);
    public QrHandler Qr => new(Store, Keys, Clock);
    public TillSnapshotHandler Snapshots => new(Store, Identity, Keys, Clock);
    public SettleOfflineSalesHandler Offline => new(Store, Identity, Poster, Alerts, Notifier, Keys, Clock);
    public MenuHandlers Menu => new(Store, Identity, Clock);
    public ParentPreOrderHandlers PreOrders => new(Store, Poster, Clock);
    public TillPreOrderHandlers TillPreOrders => new(Store, Identity, Clock);
    public RefundHandlers RefundOps => new(Store, Partner, Identity, Clock);
    public ReconciliationHandlers Reconciliations => new(Store, Partner, Identity, Clock);
    public PayoutHandlers PayoutOps => new(Store, Partner, Identity, OrgAccess, Clock);
    public ErasureHandlers ErasureOps => new(Store, Files, Clock);
    public StaffAccountHandlers StaffOps => new(Store, Identity, Clock);

    // ---- setup helpers ----

    /// <summary>A registered, verified, configured school with its admin. Fee is zero unless asked for.</summary>
    public async Task<School> OpenSchool(int feeBasisPoints = 0, bool staffAccounts = false, long? ceiling = null)
    {
        var registered = (await Schools.Handle(new RegisterSchool(new(PlatformAdmin, "Sunrise Primary", "200301234", ShopId)), default)).Value!;
        await Schools.Handle(new AddSchoolAdmin(registered.Id, new(PlatformAdmin, SchoolAdmin)), default);
        await Schools.Handle(new VerifySchool(registered.Id, PlatformAdmin), default);
        await Schools.Handle(new ConfigureSchool(registered.Id, new(PlatformAdmin, feeBasisPoints, "daily", staffAccounts, ceiling, 60)), default);
        School = Store.Schools.Single();
        return School;
    }

    public sealed record Child(Guid HolderId, Guid WalletId, Guid Parent, string LinkCode, string Card);

    private int _cards;

    /// <summary>A learner with a wallet, a linked parent (consent given) and a card.</summary>
    public async Task<Child> EnrolChild(string first = "Thandi", string last = "Nkosi", string? grade = "5", string? @class = "5B", string type = "learner", bool withCard = true)
    {
        var added = (await HolderOps.Handle(new AddHolder(School.Id, new(SchoolAdmin, first, last, grade, @class, type)), default)).Value!;
        var parent = Guid.NewGuid();
        Identity.People.Add(parent);
        await Guardians.Handle(new LinkChild(new(parent, added.LinkCode, true, "v1")), default);

        var card = $"04A2{++_cards:X4}";
        if (withCard) await TokenOps.Handle(new IssueToken(added.Holder.Id, new(SchoolAdmin, "nfc", card)), default);
        return new Child(added.Holder.Id, added.Holder.WalletId, parent, added.LinkCode, card);
    }

    /// <summary>Puts money in a wallet as a completed partner top-up would.</summary>
    public async Task Fund(Child child, long cents)
    {
        if (cents <= 0) return;
        var wallet = Store.Wallets.Single(w => w.Id == child.WalletId);
        var txn = LedgerTransaction.Create(LedgerTransactionType.TopUp, $"test-topup:{Guid.NewGuid():N}", Postings.TopUp(wallet.Id, cents), Clock.GetUtcNow()).Value!;
        await Store.Add(txn, default);
        await Store.Save(default);
        Partner.Held += cents;
    }

    public Task<long> Balance(Child child) => Store.Balance(AccountIds.Wallet(child.WalletId), default);

    public Task<Result<WalletPaymentResponse>> Pay(Child child, long cents, Guid? saleId = null, params SaleItemDto[] items) =>
        Payments.Handle(new ProcessWalletPayment(ShopId, new(DeviceId, StaffId, saleId ?? Guid.NewGuid(), child.Card, null, cents, items.Length == 0 ? [new(null, "snacks", "Crisps", 1, cents)] : [.. items])), default);

    /// <summary>A signed allowance as a till would hold it, for a given wallet and amount (the server's key, as the real snapshot would sign).</summary>
    public string AllowanceToken(Guid walletId, long cents, DateOnly? day = null) =>
        Allowances.Issue(AllowanceKey, new SignedAllowance(walletId, day ?? BusinessDay.Of(Clock.GetUtcNow()), cents, 0, 1));

    public async Task<WalletReconciliation> Reconcile()
    {
        var run = await Reconciliations.Handle(new RunWalletReconciliation(BusinessDay.Of(Clock.GetUtcNow()), PlatformAdmin), default);
        return Store.Reconciliations.Single(r => r.Id == run.Value!.Id);
    }
}
