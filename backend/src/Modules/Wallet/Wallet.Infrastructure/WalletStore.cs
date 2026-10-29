using Microsoft.EntityFrameworkCore;
using TillKnowIT.SharedKernel;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure.Persistence;

namespace Wallet.Infrastructure;

internal sealed class WalletStore(WalletDbContext db) : IWalletStore
{
    private DbSet<T> Set<T>() where T : class => db.Set<T>();

    // ---- schools ----
    public async Task Add(School school, CancellationToken ct) => await Set<School>().AddAsync(school, ct);
    public Task<School?> GetSchool(Guid schoolId, CancellationToken ct) => Set<School>().FirstOrDefaultAsync(s => s.Id == schoolId, ct);
    public Task<School?> SchoolByShop(Guid shopId, CancellationToken ct) => Set<School>().FirstOrDefaultAsync(s => s.ShopId == shopId, ct);
    public Task<List<School>> AllSchools(CancellationToken ct) => Set<School>().AsNoTracking().ToListAsync(ct);
    public async Task Add(SchoolAdmin admin, CancellationToken ct) => await Set<SchoolAdmin>().AddAsync(admin, ct);
    public Task<bool> IsSchoolAdmin(Guid schoolId, Guid personId, CancellationToken ct) => Set<SchoolAdmin>().AnyAsync(a => a.SchoolId == schoolId && a.PersonId == personId, ct);

    // ---- holders, wallets, tokens, guardians ----
    public async Task Add(Holder holder, CancellationToken ct) => await Set<Holder>().AddAsync(holder, ct);
    public Task<Holder?> GetHolder(Guid holderId, CancellationToken ct) => Set<Holder>().FirstOrDefaultAsync(h => h.Id == holderId, ct);
    public Task<List<Holder>> HoldersOf(Guid schoolId, CancellationToken ct) => Set<Holder>().Where(h => h.SchoolId == schoolId).ToListAsync(ct);
    public Task<Holder?> HolderByLinkCode(string linkCodeHash, CancellationToken ct) => Set<Holder>().FirstOrDefaultAsync(h => h.LinkCodeHash == linkCodeHash, ct);
    public async Task Add(SchoolWallet wallet, CancellationToken ct) => await Set<SchoolWallet>().AddAsync(wallet, ct);
    public Task<SchoolWallet?> GetWallet(Guid walletId, CancellationToken ct) => Set<SchoolWallet>().FirstOrDefaultAsync(w => w.Id == walletId, ct);
    public Task<SchoolWallet?> WalletOfHolder(Guid holderId, CancellationToken ct) => Set<SchoolWallet>().FirstOrDefaultAsync(w => w.HolderId == holderId, ct);
    public Task<List<SchoolWallet>> WalletsOf(Guid schoolId, CancellationToken ct) => Set<SchoolWallet>().Where(w => w.SchoolId == schoolId).ToListAsync(ct);
    public Task<List<WalletRestriction>> Restrictions(Guid walletId, CancellationToken ct) => Set<WalletRestriction>().Where(r => r.WalletId == walletId).ToListAsync(ct);

    public Task<List<WalletRestriction>> RestrictionsOfSchool(Guid schoolId, CancellationToken ct) =>
        (from r in Set<WalletRestriction>().AsNoTracking() join w in Set<SchoolWallet>().AsNoTracking() on r.WalletId equals w.Id where w.SchoolId == schoolId select r).ToListAsync(ct);

    public async Task ReplaceRestrictions(Guid walletId, List<WalletRestriction> restrictions, CancellationToken ct)
    {
        Set<WalletRestriction>().RemoveRange(await Set<WalletRestriction>().Where(r => r.WalletId == walletId).ToListAsync(ct));
        await Set<WalletRestriction>().AddRangeAsync(restrictions, ct);
    }

    public async Task Add(HolderToken token, CancellationToken ct) => await Set<HolderToken>().AddAsync(token, ct);
    public Task<HolderToken?> TokenByHash(string hash, CancellationToken ct) => Set<HolderToken>().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    public Task<HolderToken?> GetToken(Guid tokenId, CancellationToken ct) => Set<HolderToken>().FirstOrDefaultAsync(t => t.Id == tokenId, ct);
    public Task<List<HolderToken>> TokensOf(Guid holderId, CancellationToken ct) => Set<HolderToken>().Where(t => t.HolderId == holderId).ToListAsync(ct);

    public Task<List<HolderToken>> TokensOfSchool(Guid schoolId, CancellationToken ct) =>
        (from t in Set<HolderToken>().AsNoTracking() join h in Set<Holder>().AsNoTracking() on t.HolderId equals h.Id where h.SchoolId == schoolId select t).ToListAsync(ct);

    public async Task Add(Guardianship guardianship, CancellationToken ct) => await Set<Guardianship>().AddAsync(guardianship, ct);
    public Task<Guardianship?> GuardianshipOf(Guid parentPersonId, Guid holderId, CancellationToken ct) => Set<Guardianship>().FirstOrDefaultAsync(g => g.ParentPersonId == parentPersonId && g.HolderId == holderId, ct);
    public Task<List<Guardianship>> GuardiansOf(Guid holderId, CancellationToken ct) => Set<Guardianship>().Where(g => g.HolderId == holderId).ToListAsync(ct);
    public Task<List<Guardianship>> GuardianshipsOfParent(Guid parentPersonId, CancellationToken ct) => Set<Guardianship>().Where(g => g.ParentPersonId == parentPersonId).ToListAsync(ct);

    public async Task RemoveGuardianships(Guid holderId, CancellationToken ct) =>
        Set<Guardianship>().RemoveRange(await Set<Guardianship>().Where(g => g.HolderId == holderId).ToListAsync(ct));

    // ---- ledger ----
    public async Task Add(LedgerAccount account, CancellationToken ct) => await Set<LedgerAccount>().AddAsync(account, ct);
    public Task<bool> AccountExists(Guid accountId, CancellationToken ct) => Set<LedgerAccount>().AnyAsync(a => a.Id == accountId, ct);
    public async Task Add(LedgerTransaction transaction, CancellationToken ct) => await Set<LedgerTransaction>().AddAsync(transaction, ct);
    public Task<bool> TransactionExists(string reference, CancellationToken ct) => Set<LedgerTransaction>().AnyAsync(t => t.Reference == reference, ct);

    public async Task<long> Balance(Guid accountId, CancellationToken ct) =>
        await Set<LedgerEntry>().Where(e => e.AccountId == accountId).SumAsync(e => (long?)e.AmountCents, ct) ?? 0;

    public async Task<Dictionary<Guid, long>> Balances(IReadOnlyCollection<Guid> accountIds, CancellationToken ct)
    {
        var ids = accountIds.ToList();
        return await Set<LedgerEntry>().Where(e => ids.Contains(e.AccountId)).GroupBy(e => e.AccountId).Select(g => new { g.Key, Sum = g.Sum(e => e.AmountCents) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
    }

    public Task<List<LedgerEntryRow>> EntriesOf(Guid accountId, DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct) =>
        (from e in Set<LedgerEntry>().AsNoTracking()
         join t in Set<LedgerTransaction>().AsNoTracking() on e.TransactionId equals t.Id
         where e.AccountId == accountId && (start == null || t.CreatedAt >= start) && (end == null || t.CreatedAt < end)
         orderby t.CreatedAt
         select new LedgerEntryRow(t.Id, t.Type, t.Reference, t.Description, e.AmountCents, t.CreatedAt)).ToListAsync(ct);

    public Task<List<Guid>> UnbalancedTransactions(int take, CancellationToken ct) =>
        Set<LedgerEntry>().GroupBy(e => e.TransactionId).Where(g => g.Sum(e => e.AmountCents) != 0).Select(g => g.Key).Take(take).ToListAsync(ct);

    // ---- purchases and offline sales ----
    public async Task Add(WalletPurchase purchase, CancellationToken ct) => await Set<WalletPurchase>().AddAsync(purchase, ct);
    public Task<WalletPurchase?> GetPurchase(Guid purchaseId, CancellationToken ct) => Set<WalletPurchase>().FirstOrDefaultAsync(p => p.Id == purchaseId, ct);

    public async Task<long> SpentBetween(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await Set<WalletPurchase>().Where(p => p.WalletId == walletId && p.ReversedAt == null && p.SoldAt >= from && p.SoldAt < to).SumAsync(p => (long?)p.AmountCents, ct) ?? 0;

    public Task<Dictionary<Guid, long>> SpentByWallet(Guid schoolId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
        (from p in Set<WalletPurchase>().AsNoTracking()
         join w in Set<SchoolWallet>().AsNoTracking() on p.WalletId equals w.Id
         where w.SchoolId == schoolId && p.ReversedAt == null && p.SoldAt >= start && p.SoldAt < end
         group p by p.WalletId into g
         select new { g.Key, Sum = g.Sum(x => x.AmountCents) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    public Task<List<WalletPurchase>> PurchasesOf(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Set<WalletPurchase>().AsNoTracking().Where(p => p.WalletId == walletId && p.SoldAt >= from && p.SoldAt < to).ToListAsync(ct);

    public Task<List<WalletPurchase>> PurchasesOfShop(Guid shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Set<WalletPurchase>().AsNoTracking().Where(p => p.ShopId == shopId && p.SoldAt >= from && p.SoldAt < to).ToListAsync(ct);

    public async Task Add(OfflineSaleRecord record, CancellationToken ct) => await Set<OfflineSaleRecord>().AddAsync(record, ct);
    public Task<OfflineSaleRecord?> GetOfflineRecord(Guid saleId, CancellationToken ct) => Set<OfflineSaleRecord>().FirstOrDefaultAsync(r => r.Id == saleId, ct);

    public async Task<long> OfflineSettledBetween(Guid walletId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await Set<OfflineSaleRecord>().Where(r => r.WalletId == walletId && r.Status == OfflineSaleStatus.Settled && r.SoldAt >= from && r.SoldAt < to).SumAsync(r => (long?)r.AmountCents, ct) ?? 0;

    // ---- top-ups ----
    public async Task Add(TopUp topUp, CancellationToken ct) => await Set<TopUp>().AddAsync(topUp, ct);
    public Task<TopUp?> TopUpByPartnerRef(string partnerRef, CancellationToken ct) => Set<TopUp>().FirstOrDefaultAsync(t => t.PartnerRef == partnerRef, ct);
    public Task<bool> HasReceipt(string partner, string eventId, CancellationToken ct) => Set<WalletWebhookReceipt>().AnyAsync(r => r.Partner == partner && r.EventId == eventId, ct);
    public async Task Add(WalletWebhookReceipt receipt, CancellationToken ct) => await Set<WalletWebhookReceipt>().AddAsync(receipt, ct);

    // ---- menu and pre-orders ----
    public Task<List<BreakSlot>> Breaks(Guid schoolId, CancellationToken ct) => Set<BreakSlot>().AsNoTracking().Where(b => b.SchoolId == schoolId).ToListAsync(ct);

    public async Task ReplaceBreaks(Guid schoolId, List<BreakSlot> breaks, CancellationToken ct)
    {
        Set<BreakSlot>().RemoveRange(await Set<BreakSlot>().Where(b => b.SchoolId == schoolId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);   // removed first, so a code that is kept does not clash with its old row
        await Set<BreakSlot>().AddRangeAsync(breaks, ct);
    }

    public Task<List<MenuItem>> MenuOf(Guid schoolId, CancellationToken ct) => Set<MenuItem>().Where(m => m.SchoolId == schoolId).ToListAsync(ct);
    public Task<MenuItem?> GetMenuItem(Guid itemId, CancellationToken ct) => Set<MenuItem>().FirstOrDefaultAsync(m => m.Id == itemId, ct);
    public async Task Add(MenuItem item, CancellationToken ct) => await Set<MenuItem>().AddAsync(item, ct);
    public async Task Add(PreOrder order, CancellationToken ct) => await Set<PreOrder>().AddAsync(order, ct);
    public Task<PreOrder?> GetPreOrder(Guid orderId, CancellationToken ct) => Set<PreOrder>().FirstOrDefaultAsync(o => o.Id == orderId, ct);
    public Task<List<PreOrder>> PreOrdersOf(Guid schoolId, DateOnly collectOn, CancellationToken ct) => Set<PreOrder>().Where(o => o.SchoolId == schoolId && o.CollectOn == collectOn).ToListAsync(ct);
    public Task<List<PreOrder>> PreOrdersOfHolder(Guid holderId, DateOnly from, CancellationToken ct) => Set<PreOrder>().Where(o => o.HolderId == holderId && o.CollectOn >= from).ToListAsync(ct);

    // ---- refunds, payouts, reconciliation, erasure ----
    public async Task Add(RefundRequest request, CancellationToken ct) => await Set<RefundRequest>().AddAsync(request, ct);
    public Task<RefundRequest?> GetRefund(Guid refundId, CancellationToken ct) => Set<RefundRequest>().FirstOrDefaultAsync(r => r.Id == refundId, ct);
    public Task<List<RefundRequest>> RefundsOf(Guid walletId, CancellationToken ct) => Set<RefundRequest>().AsNoTracking().Where(r => r.WalletId == walletId).ToListAsync(ct);
    public async Task Add(ShopPayout payout, CancellationToken ct) => await Set<ShopPayout>().AddAsync(payout, ct);
    public Task<List<ShopPayout>> PayoutsOf(Guid shopId, CancellationToken ct) => Set<ShopPayout>().AsNoTracking().Where(p => p.ShopId == shopId).ToListAsync(ct);
    public async Task Add(WalletReconciliation reconciliation, CancellationToken ct) => await Set<WalletReconciliation>().AddAsync(reconciliation, ct);
    public Task<WalletReconciliation?> GetReconciliation(Guid id, CancellationToken ct) => Set<WalletReconciliation>().FirstOrDefaultAsync(r => r.Id == id, ct);
    public Task<WalletReconciliation?> OpenMismatch(CancellationToken ct) => Set<WalletReconciliation>().AsNoTracking().Where(r => r.Status == ReconciliationStatus.Mismatch).OrderBy(r => r.Day).FirstOrDefaultAsync(ct);
    public Task<bool> HasReconciliation(DateOnly day, CancellationToken ct) => Set<WalletReconciliation>().AnyAsync(r => r.Day == day, ct);
    public async Task Add(ErasureRequest request, CancellationToken ct) => await Set<ErasureRequest>().AddAsync(request, ct);
    public Task<ErasureRequest?> ErasureOf(Guid holderId, CancellationToken ct) => Set<ErasureRequest>().FirstOrDefaultAsync(e => e.HolderId == holderId, ct);
    public Task<List<ErasureRequest>> PendingErasures(CancellationToken ct) => Set<ErasureRequest>().Where(e => e.CompletedAt == null).ToListAsync(ct);

    /// <summary>
    /// A concurrent change (a wallet's version moved) or a repeated reference (a unique index) means someone else got there first.
    /// Both come out as one exception the application knows how to answer: "try again".
    /// </summary>
    public async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex) { throw new ConcurrencyConflictException("The record was changed by someone else.", ex); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyConflictException("The same record was added at the same moment.", ex);
        }
    }
}

internal sealed class WalletListings(WalletDbContext db) : Wallet.Application.IWalletListings
{
    public Task<List<WalletReconciliation>> Reconciliations(int take, CancellationToken ct) =>
        db.Set<WalletReconciliation>().AsNoTracking().OrderByDescending(r => r.Day).ThenByDescending(r => r.CreatedAt).Take(take).ToListAsync(ct);
}
