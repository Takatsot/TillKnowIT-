using Marketplace.Application.Offers;
using Marketplace.Domain;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure;

internal sealed class DealStore(MarketplaceDbContext db) : IDealStore
{
    public async Task Add(Deal deal, CancellationToken ct) => await db.Set<Deal>().AddAsync(deal, ct);

    public Task<Deal?> Get(Guid dealId, CancellationToken ct) => db.Set<Deal>().FirstOrDefaultAsync(d => d.Id == dealId, ct);

    public Task<List<Deal>> ForSupplier(Guid supplierId, CancellationToken ct) => db.Set<Deal>().AsNoTracking().Where(d => d.SupplierId == supplierId).ToListAsync(ct);

    public async Task<List<(Deal Deal, string SupplierName)>> ActiveInArea(string municipalityCode, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await (from d in db.Set<Deal>().AsNoTracking()
                          join s in db.Set<Supplier>().AsNoTracking() on d.SupplierId equals s.Id
                          where !d.IsCancelled && d.StartsAt <= now && d.EndsAt > now && s.Status == SupplierStatus.Verified
                          select new { Deal = d, s.Name }).ToListAsync(ct);
        // The area list is a short comma-separated text, so it is checked here rather than in SQL.
        return rows.Where(r => r.Deal.AppliesToArea(municipalityCode)).Select(r => (r.Deal, r.Name)).ToList();
    }

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class OfferStore(MarketplaceDbContext db) : IOfferStore
{
    public async Task<List<OfferSource>> Sources(string barcode, string municipalityCode, CancellationToken ct)
    {
        var rows = await (from p in db.Set<SupplierProduct>().AsNoTracking()
                          join s in db.Set<Supplier>().AsNoTracking() on p.SupplierId equals s.Id
                          join a in db.Set<SupplierArea>().AsNoTracking() on s.Id equals a.SupplierId
                          where p.Barcode == barcode && p.InStock && s.Status == SupplierStatus.Verified && a.MunicipalityCode == municipalityCode
                          select new { p, s, a.Days }).ToListAsync(ct);
        // A supplier's average counts only once enough deliveries have been rated; until then it is unrated.
        var supplierIds = rows.Select(r => r.s.Id).Distinct().ToList();
        var averages = (await db.Set<Rating>().AsNoTracking().Where(x => supplierIds.Contains(x.SupplierId))
                .GroupBy(x => x.SupplierId).Select(g => new { g.Key, Count = g.Count(), Average = g.Average(x => (double)x.Score) }).ToListAsync(ct))
            .Where(g => g.Count >= Rating.MinRatingsToShow).ToDictionary(g => g.Key, g => g.Average);
        return rows.Select(r => new OfferSource(r.s.Id, r.s.Name, r.s.MinOrderCents, r.s.DeliveryFeeCents, r.p.Id, r.p.UnitsPerCase,
            r.p.CasePriceCents, r.p.VatIncluded, r.Days, averages.TryGetValue(r.s.Id, out var avg) ? avg : null)).ToList();
    }

    public async Task<List<Deal>> ActiveDeals(IEnumerable<Guid> supplierIds, string municipalityCode, DateTimeOffset now, CancellationToken ct)
    {
        var ids = supplierIds.ToList();
        var deals = await db.Set<Deal>().AsNoTracking().Where(d => ids.Contains(d.SupplierId) && !d.IsCancelled && d.StartsAt <= now && d.EndsAt > now).ToListAsync(ct);
        return deals.Where(d => d.AppliesToArea(municipalityCode)).ToList();
    }

    public Task<List<ShopSupplier>> ShopSuppliers(Guid shopId, CancellationToken ct) =>
        db.Set<ShopSupplier>().AsNoTracking().Where(s => s.ShopId == shopId).ToListAsync(ct);

    public async Task<Dictionary<Guid, long>> LastPrices(Guid shopId, string barcode, CancellationToken ct) =>
        await db.Set<LastPrice>().AsNoTracking().Where(p => p.ShopId == shopId && p.Barcode == barcode).ToDictionaryAsync(p => p.SupplierId, p => p.CasePriceCents, ct);

    public async Task<Dictionary<string, string>> Settings(CancellationToken ct) =>
        await db.Set<MarketplaceSetting>().AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
}

internal sealed class ShopMarketplaceStore(MarketplaceDbContext db) : IShopMarketplaceStore
{
    public Task<SharingSetting?> GetSharing(Guid shopId, CancellationToken ct) => db.Set<SharingSetting>().FirstOrDefaultAsync(s => s.ShopId == shopId, ct);

    public async Task Add(SharingSetting setting, CancellationToken ct) => await db.Set<SharingSetting>().AddAsync(setting, ct);

    public Task<ShopSupplier?> GetShopSupplier(Guid shopId, Guid supplierId, CancellationToken ct) =>
        db.Set<ShopSupplier>().FirstOrDefaultAsync(s => s.ShopId == shopId && s.SupplierId == supplierId, ct);

    public async Task Add(ShopSupplier shopSupplier, CancellationToken ct) => await db.Set<ShopSupplier>().AddAsync(shopSupplier, ct);

    public Task<MarketplaceSetting?> GetSetting(string key, CancellationToken ct) => db.Set<MarketplaceSetting>().FirstOrDefaultAsync(s => s.Key == key, ct);

    public async Task Add(MarketplaceSetting setting, CancellationToken ct) => await db.Set<MarketplaceSetting>().AddAsync(setting, ct);

    public async Task<Dictionary<string, string>> Settings(CancellationToken ct) =>
        await db.Set<MarketplaceSetting>().AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
