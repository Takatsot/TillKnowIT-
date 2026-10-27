using TillKnowIT.SharedKernel;
namespace ShopCore.Domain;

public sealed record SaleLineRow(
    Guid SaleId, DateTimeOffset SoldAt, Guid ProductId, string Name, int Quantity, long PriceCents, long CostCents, bool Cancelled);

public sealed record PeriodSummary(int Days, long SalesCents, long ProfitCents, int Customers, long AverageSaleCents);

public sealed record BestSeller(Guid ProductId, string Name, int Units, long RevenueCents, long ProfitCents);

public sealed record DaySummary(DateOnly Day, int Customers, long SalesCents, long ProfitCents);

/// <summary>
/// The numbers on the owner's insights screen. The till has the same rules in TypeScript (insights.ts) so the
/// last 90 days work offline. A "customer" is one completed sale. Cancelled sales count for nothing.
/// </summary>
public static class InsightsCalculator
{
    /// <summary>The first moment of the period: 1 day is today, 7 days is today and the six before it.</summary>
    public static DateTimeOffset PeriodStart(DateTimeOffset now, int days) => BusinessDay.Start(BusinessDay.Of(now).AddDays(-(days - 1)));

    public static PeriodSummary Summarise(IEnumerable<SaleLineRow> rows, DateTimeOffset now, int days)
    {
        var from = PeriodStart(now, days);
        var live = rows.Where(r => !r.Cancelled && r.SoldAt >= from && r.SoldAt <= now).ToList();

        var sales = live.Sum(r => r.PriceCents * r.Quantity);
        var profit = live.Sum(r => (r.PriceCents - r.CostCents) * r.Quantity);
        var customers = live.Select(r => r.SaleId).Distinct().Count();
        // Average sale is rounded to the nearest cent, halves up.
        var average = customers == 0 ? 0 : (sales * 2 + customers) / (customers * 2);
        return new PeriodSummary(days, sales, profit, customers, average);
    }

    /// <summary>Top sellers by units sold, then by revenue, then by name so the order never jumps about.</summary>
    public static List<BestSeller> BestSellers(IEnumerable<SaleLineRow> rows, DateTimeOffset now, int days, int top)
    {
        var from = PeriodStart(now, days);
        return rows.Where(r => !r.Cancelled && r.SoldAt >= from && r.SoldAt <= now)
            .GroupBy(r => r.ProductId)
            .Select(g => new BestSeller(g.Key, g.OrderByDescending(r => r.SoldAt).First().Name, g.Sum(r => r.Quantity),
                g.Sum(r => r.PriceCents * r.Quantity), g.Sum(r => (r.PriceCents - r.CostCents) * r.Quantity)))
            .OrderByDescending(b => b.Units).ThenByDescending(b => b.RevenueCents).ThenBy(b => b.Name, StringComparer.Ordinal)
            .Take(top)
            .ToList();
    }

    /// <summary>One row per business day that had sales, oldest first.</summary>
    public static List<DaySummary> ByDay(IEnumerable<SaleLineRow> rows, DateOnly from, DateOnly to) =>
        rows.Where(r => !r.Cancelled)
            .Select(r => (Day: BusinessDay.Of(r.SoldAt), Row: r))
            .Where(x => x.Day >= from && x.Day <= to)
            .GroupBy(x => x.Day)
            .Select(g => new DaySummary(g.Key, g.Select(x => x.Row.SaleId).Distinct().Count(),
                g.Sum(x => x.Row.PriceCents * x.Row.Quantity), g.Sum(x => (x.Row.PriceCents - x.Row.CostCents) * x.Row.Quantity)))
            .OrderBy(d => d.Day)
            .ToList();
}
