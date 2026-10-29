using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

internal sealed class ProductPullStore(ShopCoreDbContext db) : IProductPullStore
{
    public async Task<List<PulledProduct>> ChangedSince(Guid shopId, DateTimeOffset? since, int take, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().Where(p => p.ShopId == shopId);
        if (since is { } s) query = query.Where(p => (p.UpdatedAt ?? p.CreatedAt) > s);
        var rows = await query.OrderBy(p => p.UpdatedAt ?? p.CreatedAt).ThenBy(p => p.Id).Take(take).ToListAsync(ct);
        return rows.Select(p => new PulledProduct(p.Id, p.Name, p.Barcode, p.CategoryCode, p.CostCents, p.PriceCents, p.LowStockLevel, p.ExpiryDate, p.IsActive,
            p.SoldByWeight, p.IsAddOn, p.RequiresSerial, p.ParentProductId, p.VariantLabel, p.UpdatedAt ?? p.CreatedAt)).ToList();
    }
}
