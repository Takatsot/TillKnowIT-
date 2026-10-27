using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Sync;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

internal sealed class UploadStore(ShopCoreDbContext db) : IUploadStore
{
    public async Task<HashSet<Guid>> ExistingSaleIds(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return list.Count == 0 ? [] : (await db.Sales.AsNoTracking().Where(s => list.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task<HashSet<Guid>> ExistingMovementIds(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return list.Count == 0 ? [] : (await db.StockMovements.AsNoTracking().Where(m => list.Contains(m.Id)).Select(m => m.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task<HashSet<Guid>> ExistingCancellationIds(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return list.Count == 0 ? [] : (await db.Set<SaleCancellation>().AsNoTracking().Where(c => list.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task<HashSet<Guid>> CancelledSaleIds(IEnumerable<Guid> saleIds, CancellationToken ct)
    {
        var list = saleIds.ToList();
        return list.Count == 0 ? [] : (await db.Set<SaleCancellation>().AsNoTracking().Where(c => list.Contains(c.SaleId)).Select(c => c.SaleId).ToListAsync(ct)).ToHashSet();
    }

    public async Task<HashSet<Guid>> ExistingProductIds(Guid shopId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return list.Count == 0 ? [] : (await db.Products.AsNoTracking().Where(p => p.ShopId == shopId && list.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task<Dictionary<Guid, Dictionary<Guid, int>>> SaleQuantities(Guid shopId, IEnumerable<Guid> saleIds, CancellationToken ct)
    {
        var list = saleIds.ToList();
        if (list.Count == 0) return [];

        var rows = await db.Sales.AsNoTracking()
            .Where(s => s.ShopId == shopId && list.Contains(s.Id))
            .SelectMany(s => s.Lines.Select(l => new { SaleId = s.Id, l.ProductId, l.Quantity }))
            .ToListAsync(ct);
        return rows.GroupBy(r => r.SaleId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.ProductId, r => r.Quantity));
    }

    public async Task<Dictionary<Guid, Product>> GetProducts(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return list.Count == 0 ? [] : await db.Products.Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    public async Task<Dictionary<string, Guid>> BarcodeOwners(Guid shopId, IEnumerable<string> barcodes, CancellationToken ct)
    {
        var list = barcodes.Distinct().ToList();
        if (list.Count == 0) return [];
        var rows = await db.Products.AsNoTracking()
            .Where(p => p.ShopId == shopId && p.Barcode != null && list.Contains(p.Barcode))
            .Select(p => new { Barcode = p.Barcode!, p.Id })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Barcode, r => r.Id);
    }

    public async Task<Dictionary<Guid, CreditAccount>> GetCreditAccounts(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return list.Count == 0 ? [] : await db.Set<CreditAccount>().Where(a => list.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
    }

    public async Task<HashSet<Guid>> ExistingCreditEntryIds(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return list.Count == 0 ? [] : (await db.Set<CreditEntry>().AsNoTracking().Where(e => list.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task<HashSet<(Guid SaleId, CreditEntryType Type)>> CreditEntryKeys(IEnumerable<Guid> saleIds, CancellationToken ct)
    {
        var list = saleIds.ToList();
        if (list.Count == 0) return [];
        var rows = await db.Set<CreditEntry>().AsNoTracking()
            .Where(e => e.SaleId != null && list.Contains(e.SaleId.Value))
            .Select(e => new { SaleId = e.SaleId!.Value, e.Type })
            .ToListAsync(ct);
        return rows.Select(r => (r.SaleId, r.Type)).ToHashSet();
    }

    public async Task<Dictionary<Guid, SaleSummary>> SaleSummaries(Guid shopId, IEnumerable<Guid> saleIds, CancellationToken ct)
    {
        var list = saleIds.ToList();
        if (list.Count == 0) return [];
        var rows = await db.Sales.AsNoTracking()
            .Where(s => s.ShopId == shopId && list.Contains(s.Id))
            .Select(s => new { s.Id, s.PaymentMethodCode, s.AccountId, s.TotalCents })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new SaleSummary(r.PaymentMethodCode, r.AccountId, r.TotalCents));
    }

    public async Task<HashSet<Guid>> ExistingCashUpIds(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return list.Count == 0 ? [] : (await db.Set<CashUp>().AsNoTracking().Where(c => list.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct)).ToHashSet();
    }

    public Task<bool> CashUpExists(Guid shopId, Guid deviceId, DateOnly day, CancellationToken ct) =>
        db.Set<CashUp>().AsNoTracking().AnyAsync(c => c.ShopId == shopId && c.TillDeviceId == deviceId && c.Day == day, ct);

    public Task<List<CashSaleRow>> CashSales(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        db.Sales.AsNoTracking()
            .Where(s => s.TillDeviceId == deviceId && s.PaymentMethodCode == PaymentMethods.Cash && s.SoldAt >= from && s.SoldAt <= to)
            .Select(s => new CashSaleRow(s.Id, s.TotalCents, db.Set<SaleCancellation>().Any(c => c.SaleId == s.Id)))
            .ToListAsync(ct);

    public async Task<long> CreditCashPayments(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        -(await db.Set<CreditEntry>().AsNoTracking()
            .Where(e => e.DeviceId == deviceId && e.Type == CreditEntryType.Payment && e.PaymentMethodCode == PaymentMethods.Cash
                        && e.CreatedAt >= from && e.CreatedAt <= to)
            .SumAsync(e => (long?)e.AmountCents, ct) ?? 0);

    public void Add(CreditAccount account) => db.Set<CreditAccount>().Add(account);

    public void Add(CreditEntry entry) => db.Set<CreditEntry>().Add(entry);

    public void Add(CashUp cashUp) => db.Set<CashUp>().Add(cashUp);

    public void Add(Product product) => db.Products.Add(product);

    public void Add(Sale sale) => db.Sales.Add(sale);

    public void Add(StockMovement movement) => db.StockMovements.Add(movement);

    public void Add(SaleCancellation cancellation) => db.Set<SaleCancellation>().Add(cancellation);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
