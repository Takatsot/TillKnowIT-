using Microsoft.EntityFrameworkCore;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.SharedKernel;

namespace ShopCore.Infrastructure;

internal sealed class CategorySalesSource(ShopCoreDbContext db) : ICategorySalesSource
{
    public async Task<List<CategoryDay>> Day(Guid shopId, DateOnly day, CancellationToken ct)
    {
        var from = BusinessDay.Start(day);
        var to = BusinessDay.Start(day.AddDays(1));
        var rows = await db.Sales.AsNoTracking()
            .Where(s => s.ShopId == shopId && s.SoldAt >= from && s.SoldAt < to && !db.Set<SaleCancellation>().Any(c => c.SaleId == s.Id))
            .SelectMany(s => s.Lines.Select(l => new
            {
                Category = db.Products.Where(p => p.Id == l.ProductId).Select(p => p.CategoryCode).FirstOrDefault(),
                l.Quantity, l.PriceCents, l.UnitDivisor, l.DiscountCents,
            }))
            .ToListAsync(ct);
        return rows.Where(r => r.Category != null).GroupBy(r => r.Category!)
            .Select(g => new CategoryDay(g.Key, g.Sum(r => (r.Quantity + r.UnitDivisor / 2) / r.UnitDivisor),
                g.Sum(r => SaleMath.LineTotal(r.PriceCents, r.Quantity, r.UnitDivisor, r.DiscountCents))))
            .OrderBy(c => c.CategoryCode).ToList();
    }
}
