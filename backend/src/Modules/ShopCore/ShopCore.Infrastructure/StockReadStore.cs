using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Stock;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

internal sealed class StockReadStore(ShopCoreDbContext db) : IStockReadStore
{
    public async Task<List<StockItem>> Items(Guid shopId, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-30);

        // Stock on hand is the sum of movements. Net sold in the last 30 days is sales less the stock put back by
        // cancellations, so a cancelled sale does not make a product look faster than it is.
        var rows = await db.Products.AsNoTracking()
            .Where(p => p.ShopId == shopId && p.IsActive)
            .Select(p => new
            {
                p.Id, p.Name, p.Barcode, p.IsActive, p.CreatedAt, p.LowStockLevel, p.ExpiryDate, p.PriceCents,
                OnHand = db.StockMovements.Where(m => m.ProductId == p.Id).Sum(m => (int?)m.Quantity) ?? 0,
                Sold = db.StockMovements
                    .Where(m => m.ProductId == p.Id && m.CreatedAt >= since && (m.Type == StockMovementType.Sale || m.Type == StockMovementType.Cancel))
                    .Sum(m => (int?)-m.Quantity) ?? 0,
                LastSoldAt = db.StockMovements
                    .Where(m => m.ProductId == p.Id && m.Type == StockMovementType.Sale)
                    .Max(m => (DateTimeOffset?)m.CreatedAt),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new StockItem(r.Id, r.Name, r.Barcode, r.IsActive, r.CreatedAt, r.OnHand, r.LowStockLevel, r.ExpiryDate,
                Math.Max(r.Sold, 0), r.LastSoldAt, r.PriceCents))
            .ToList();
    }
}
