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

    public void Add(Sale sale) => db.Sales.Add(sale);

    public void Add(StockMovement movement) => db.StockMovements.Add(movement);

    public void Add(SaleCancellation cancellation) => db.Set<SaleCancellation>().Add(cancellation);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
