using System.Text;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Credit;
using ShopCore.Application.Insights;
using ShopCore.Application.Reports;
using ShopCore.Application.Stock;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Reporting;

namespace ShopCore.Application.Tests;

public class InsightsAndReportsTests
{
    // 26 Oct 2026, 12:00 in the shop (10:00 UTC).
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Bread = Guid.NewGuid();
    private static readonly Guid Milk = Guid.NewGuid();

    private static SaleLineRow Line(Guid sale, Guid product, string name, int qty, long price, long cost, DateTimeOffset at, bool cancelled = false) =>
        new(sale, at, product, name, qty, price, cost, cancelled);

    private static List<SaleLineRow> Rows()
    {
        var s1 = Guid.NewGuid(); var s2 = Guid.NewGuid(); var s3 = Guid.NewGuid(); var s4 = Guid.NewGuid();
        return
        [
            Line(s1, Bread, "Bread", 2, 1800, 1400, Now.AddHours(-1)),                    // today: sale 3600, profit 800
            Line(s1, Milk, "Milk", 1, 2500, 2100, Now.AddHours(-1)),                      //        sale 2500, profit 400
            Line(s2, Bread, "Bread", 1, 1800, 1400, Now.AddDays(-3)),                      // this week
            Line(s3, Milk, "Milk", 4, 2500, 2100, Now.AddDays(-20)),                       // this month
            Line(s4, Bread, "Bread", 9, 1800, 1400, Now.AddHours(-2), cancelled: true),    // cancelled: counts for nothing
            Line(Guid.NewGuid(), Bread, "Bread", 1, 1800, 1400, Now.AddDays(-45)),         // too old for any period
        ];
    }

    // ---- calculator ----

    [Fact]
    public void Periods_start_at_the_beginning_of_the_business_day()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 22, 0, 0, TimeSpan.Zero), InsightsCalculator.PeriodStart(Now, 1));
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 22, 0, 0, TimeSpan.Zero), InsightsCalculator.PeriodStart(Now, 7));
    }

    [Fact]
    public void Summary_counts_sales_profit_customers_and_average_and_ignores_cancelled_sales()
    {
        var today = InsightsCalculator.Summarise(Rows(), Now, 1);
        Assert.Equal(6100, today.SalesCents);
        Assert.Equal(1200, today.ProfitCents);
        Assert.Equal(1, today.Customers);
        Assert.Equal(6100, today.AverageSaleCents);

        var week = InsightsCalculator.Summarise(Rows(), Now, 7);
        Assert.Equal(6100 + 1800, week.SalesCents);
        Assert.Equal(2, week.Customers);
        Assert.Equal(3950, week.AverageSaleCents);

        var month = InsightsCalculator.Summarise(Rows(), Now, 30);
        Assert.Equal(6100 + 1800 + 10000, month.SalesCents);
        Assert.Equal(3, month.Customers);
    }

    [Fact]
    public void An_empty_period_is_all_zeros_and_the_average_rounds_to_the_nearest_cent()
    {
        var none = InsightsCalculator.Summarise([], Now, 7);
        Assert.Equal(new PeriodSummary(7, 0, 0, 0, 0), none);

        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var odd = InsightsCalculator.Summarise([Line(a, Bread, "B", 1, 100, 0, Now), Line(b, Bread, "B", 1, 101, 0, Now)], Now, 1);
        Assert.Equal(101, odd.AverageSaleCents);   // 201 / 2 = 100.5, rounds up
    }

    [Fact]
    public void Best_sellers_rank_by_units_then_revenue_then_name()
    {
        var best = InsightsCalculator.BestSellers(Rows(), Now, 30, 10);

        Assert.Equal(["Milk", "Bread"], best.Select(b => b.Name));
        Assert.Equal(5, best[0].Units);          // 1 today + 4 earlier
        Assert.Equal(3, best[1].Units);          // 2 + 1, the cancelled 9 do not count
        Assert.Single(InsightsCalculator.BestSellers(Rows(), Now, 30, 1));
    }

    [Fact]
    public void By_day_gives_one_row_per_trading_day_oldest_first()
    {
        var days = InsightsCalculator.ByDay(Rows(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal([new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 26)], days.Select(d => d.Day));
        Assert.Equal(6100, days.Single(d => d.Day == new DateOnly(2026, 10, 26)).SalesCents);
    }

    [Fact]
    public void A_sale_late_in_the_evening_UTC_belongs_to_the_next_business_day()
    {
        var late = Line(Guid.NewGuid(), Bread, "Bread", 1, 1800, 1400, new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero));
        var day = Assert.Single(InsightsCalculator.ByDay([late], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        Assert.Equal(new DateOnly(2026, 10, 26), day.Day);
    }

    // ---- fakes ----

    private sealed class FakeSales(List<SaleLineRow> rows) : IInsightsReadStore
    {
        public Task<List<SaleLineRow>> SaleLines(Guid shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult(rows.Where(r => r.SoldAt >= from && r.SoldAt <= to).ToList());
    }

    private sealed class FakeStock(List<StockItem> items) : IStockReadStore
    {
        public Task<List<StockItem>> Items(Guid shopId, DateTimeOffset now, CancellationToken ct) => Task.FromResult(items);
    }

    private sealed class FakeCredit(List<(Guid, string, string?, long?, long)> rows) : ICreditReadStore
    {
        public Task<List<(Guid Id, string Name, string? Phone, long? LimitCents, long BalanceCents)>> Balances(Guid shopId, CancellationToken ct) => Task.FromResult(rows);
        public Task<bool> AccountExists(Guid shopId, Guid accountId, CancellationToken ct) => Task.FromResult(true);
        public Task<List<CreditHistoryEntry>> History(Guid accountId, CancellationToken ct) => Task.FromResult(new List<CreditHistoryEntry>());
    }

    private sealed class FakeReports(List<CashUpRow> rows) : IReportReadStore
    {
        public Task<List<CashUpRow>> CashUps(Guid shopId, DateOnly from, DateOnly to, CancellationToken ct) => Task.FromResult(rows);
    }

    private static StockItem Stock(string name, int onHand, int low, int sold30 = 0, DateTimeOffset? lastSold = null, DateOnly? expiry = null) =>
        new(Guid.NewGuid(), name, "6001234567890", true, Now.AddDays(-90), onHand, low, expiry, sold30, lastSold);

    // ---- insights handler ----

    [Fact]
    public async Task Insights_bring_the_numbers_best_sellers_and_what_to_reorder_together()
    {
        var items = new List<StockItem>
        {
            Stock("Bread", 3, 5, sold30: 60, lastSold: Now),
            Stock("Old tins", 12, 2, lastSold: Now.AddDays(-60)),
            Stock("Milk", 20, 5, sold30: 30, lastSold: Now, expiry: new DateOnly(2026, 10, 29)),
            Stock("Rice", 40, 5, sold30: 30, lastSold: Now),
        };
        var handler = new GetInsightsHandler(new FakeSales(Rows()), new FakeStock(items), new FakeTimeProvider(Now));

        var result = (await handler.Handle(new GetInsights(Guid.NewGuid()), default)).Value!;

        Assert.Equal(6100, result.Today.SalesCents);
        Assert.Equal(7900, result.Week.SalesCents);
        Assert.Equal(["Milk", "Bread"], result.BestSellers.Select(b => b.Name));
        Assert.Equal(["Bread"], result.RunningLow.Select(r => r.Name));
        Assert.Equal(25, result.RunningLow.Single().SuggestedOrderQuantity);
        Assert.Equal(["Old tins"], result.NotSelling.Select(r => r.Name));
        Assert.Equal(["Milk"], result.Expiring.Select(r => r.Name));
    }

    // ---- report handler ----

    private static RunReportHandler Reports(List<CashUpRow>? cashUps = null, List<(Guid, string, string?, long?, long)>? credit = null) =>
        new(new FakeSales(Rows()), new FakeStock([Stock("Bread", 3, 5, 60, Now)]), new FakeCredit(credit ?? []), new FakeReports(cashUps ?? []), new FakeTimeProvider(Now));

    private static string Text(ReportFile f) => Encoding.UTF8.GetString(f.Content);

    [Fact]
    public async Task Sales_by_day_csv_has_a_row_per_trading_day_for_the_period()
    {
        var result = await Reports().Handle(new RunReport(Guid.NewGuid(), "sales-by-day", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "csv"), default);

        var csv = Text(result.Value!);
        Assert.Contains("Day,Customers,Sales,Profit", csv);
        Assert.Contains("2026-10-26,1,61.00,12.00", csv);
        Assert.Equal("sales-by-day-2026-10-31.csv", result.Value!.FileName);
    }

    [Fact]
    public async Task Every_report_renders_in_every_format()
    {
        var cashUps = new List<CashUpRow> { new(new DateOnly(2026, 10, 26), Guid.NewGuid(), 5000, 8600, 8500, -100) };
        var credit = new List<(Guid, string, string?, long?, long)> { (Guid.NewGuid(), "Mrs Dlamini", "0821234567", 50000, 60000) };
        var handler = Reports(cashUps, credit);

        foreach (var code in ReportCodes.All)
            foreach (var format in new[] { "csv", "xlsx", "pdf" })
            {
                var result = await handler.Handle(new RunReport(Guid.NewGuid(), code, null, null, format), default);
                Assert.True(result.IsSuccess, $"{code} {format}");
                Assert.NotEmpty(result.Value!.Content);
            }
    }

    [Fact]
    public async Task Cash_up_and_credit_reports_show_the_right_figures()
    {
        var cashUps = new List<CashUpRow> { new(new DateOnly(2026, 10, 26), Guid.NewGuid(), 5000, 8600, 8500, -100) };
        var credit = new List<(Guid, string, string?, long?, long)> { (Guid.NewGuid(), "Mrs Dlamini", "0821234567", 50000, 60000) };
        var handler = Reports(cashUps, credit);

        var cash = Text((await handler.Handle(new RunReport(Guid.NewGuid(), "cash-ups", null, null, "csv"), default)).Value!);
        Assert.Contains(",50.00,86.00,85.00,-1.00", cash);

        var book = Text((await handler.Handle(new RunReport(Guid.NewGuid(), "credit-book", null, null, "csv"), default)).Value!);
        Assert.Contains("Mrs Dlamini,0821234567,500.00,600.00,Yes", book);
    }

    [Theory]
    [InlineData("nonsense", "csv", "2026-10-01", "2026-10-31", "report.unknown")]
    [InlineData("sales-by-day", "docx", "2026-10-01", "2026-10-31", "report.format_invalid")]
    [InlineData("sales-by-day", "csv", "2026-10-31", "2026-10-01", "report.range_invalid")]
    [InlineData("sales-by-day", "csv", "2024-01-01", "2026-10-31", "report.range_too_large")]
    public async Task Bad_requests_are_refused_with_a_reason(string code, string format, string from, string to, string expected)
    {
        var result = await Reports().Handle(new RunReport(Guid.NewGuid(), code, DateOnly.Parse(from), DateOnly.Parse(to), format), default);
        Assert.Equal(expected, result.Error!.Code);
    }

    [Fact]
    public async Task Without_dates_a_report_covers_the_last_thirty_days_ending_today()
    {
        var result = await Reports().Handle(new RunReport(Guid.NewGuid(), "sales-by-product", null, null, "csv"), default);

        Assert.Equal("sales-by-product-2026-10-26.csv", result.Value!.FileName);
        Assert.Contains("Milk,5,", Text(result.Value));
        Assert.DoesNotContain("Bread,9", Text(result.Value));
    }
}
