using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Insights;
using ShopCore.Application.Reports;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

internal sealed class InsightsReadStore(ShopCoreDbContext db) : IInsightsReadStore, IReportReadStore
{
    public async Task<List<SaleLineRow>> SaleLines(Guid shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await db.Sales.AsNoTracking()
            .Where(s => s.ShopId == shopId && s.SoldAt >= from && s.SoldAt <= to)
            .SelectMany(s => s.Lines.Select(l => new
            {
                SaleId = s.Id, s.SoldAt, l.ProductId, l.Name, l.Quantity, l.PriceCents, l.CostCents, l.UnitDivisor, l.DiscountCents,
                Cancelled = db.Set<SaleCancellation>().Any(c => c.SaleId == s.Id),
            }))
            .ToListAsync(ct);
        return rows.Select(r => new SaleLineRow(r.SaleId, r.SoldAt, r.ProductId, r.Name, r.Quantity, r.PriceCents, r.CostCents, r.Cancelled, r.UnitDivisor, r.DiscountCents)).ToList();
    }

    public async Task<List<CashUpRow>> CashUps(Guid shopId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.Set<CashUp>().AsNoTracking()
            .Where(c => c.ShopId == shopId && c.Day >= from && c.Day <= to)
            .Select(c => new { c.Id, c.Day, c.TillDeviceId, c.FloatCents, c.ExpectedCents, c.CountedCents, c.DifferenceCents })
            .ToListAsync(ct);
        return rows.Select(r => new CashUpRow(r.Day, r.TillDeviceId, r.FloatCents, r.ExpectedCents, r.CountedCents, r.DifferenceCents, r.Id)).ToList();
    }
}
