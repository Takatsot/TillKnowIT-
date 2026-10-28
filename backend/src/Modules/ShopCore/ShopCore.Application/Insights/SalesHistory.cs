using ShopCore.Contracts;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Insights;

public sealed class SalesHistory(IInsightsReadStore sales, TimeProvider clock) : ISalesHistory
{
    public async Task<List<MonthlySales>> Months(Guid shopId, int months, CancellationToken ct)
    {
        months = Math.Clamp(months, 1, 24);
        var now = clock.GetUtcNow();
        var today = BusinessDay.Of(now);
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var rows = (await sales.SaleLines(shopId, BusinessDay.Start(first), now, ct)).Where(r => !r.Cancelled).ToList();
        return Enumerable.Range(0, months).Select(i => first.AddMonths(i)).Select(m =>
        {
            var inMonth = rows.Where(r => BusinessDay.Of(r.SoldAt) is var d && d.Year == m.Year && d.Month == m.Month).ToList();
            return new MonthlySales($"{m:yyyy-MM}", inMonth.Sum(r => r.Revenue), inMonth.Select(r => r.SaleId).Distinct().Count());
        }).ToList();
    }
}
