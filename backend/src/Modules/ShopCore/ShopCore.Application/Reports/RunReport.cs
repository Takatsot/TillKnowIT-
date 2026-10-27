using ShopCore.Application.Credit;
using ShopCore.Application.Insights;
using ShopCore.Application.Stock;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.Reporting;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Reports;

public sealed record CashUpRow(DateOnly Day, Guid TillDeviceId, long FloatCents, long ExpectedCents, long CountedCents, long DifferenceCents, Guid CashUpId = default);

public interface IReportReadStore
{
    Task<List<CashUpRow>> CashUps(Guid shopId, DateOnly from, DateOnly to, CancellationToken ct);
}

public static class ReportCodes
{
    public const string SalesByDay = "sales-by-day";
    public const string SalesByProduct = "sales-by-product";
    public const string StockOnHand = "stock-on-hand";
    public const string CashUps = "cash-ups";
    public const string CreditBook = "credit-book";

    public static readonly string[] All = [SalesByDay, SalesByProduct, StockOnHand, CashUps, CreditBook];
}

/// <summary>E8-3: download a report as CSV, Excel or PDF. From and To are business days, both included.</summary>
public sealed record RunReport(Guid ShopId, string Code, DateOnly? From, DateOnly? To, string? Format) : IQuery<ReportFile>;

internal sealed class RunReportHandler(
    IInsightsReadStore sales, IStockReadStore stock, ICreditReadStore credit, IReportReadStore reports, TimeProvider clock)
    : IQueryHandler<RunReport, ReportFile>
{
    public const int MaxDays = 366;

    public async Task<Result<ReportFile>> Handle(RunReport query, CancellationToken ct)
    {
        if (!ReportCodes.All.Contains(query.Code))
            return Result<ReportFile>.Fail("report.unknown", $"Unknown report. Choose one of: {string.Join(", ", ReportCodes.All)}.");
        if (!ReportFormats.TryParse(query.Format, out var format))
            return Result<ReportFile>.Fail("report.format_invalid", "Format must be csv, xlsx or pdf.");

        var now = clock.GetUtcNow();
        var today = BusinessDay.Of(now);
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-29);
        if (from > to) return Result<ReportFile>.Fail("report.range_invalid", "The start date is after the end date.");
        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
            return Result<ReportFile>.Fail("report.range_too_large", $"A report covers at most {MaxDays} days.");

        var start = BusinessDay.Start(from);
        var end = BusinessDay.Start(to.AddDays(1)).AddTicks(-1);
        var period = $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}";

        ReportTable table;
        switch (query.Code)
        {
            case ReportCodes.SalesByDay:
                table = ReportBuilders.SalesByDay(await sales.SaleLines(query.ShopId, start, end, ct), from, to, period);
                break;
            case ReportCodes.SalesByProduct:
                table = ReportBuilders.SalesByProduct(await sales.SaleLines(query.ShopId, start, end, ct), start, end, period);
                break;
            case ReportCodes.StockOnHand:
                table = ReportBuilders.StockOnHand(await stock.Items(query.ShopId, now, ct), now, today);
                break;
            case ReportCodes.CashUps:
                table = ReportBuilders.CashUps(await reports.CashUps(query.ShopId, from, to, ct), period);
                break;
            default:
                table = ReportBuilders.CreditBook(await credit.Balances(query.ShopId, ct), today);
                break;
        }

        return Result<ReportFile>.Ok(ReportExporter.Export(table, format, $"{query.Code}-{to:yyyy-MM-dd}"));
    }
}

/// <summary>Pure report layouts: rows in, a table out. Nothing here touches the database.</summary>
public static class ReportBuilders
{
    public static ReportTable SalesByDay(IEnumerable<SaleLineRow> rows, DateOnly from, DateOnly to, string period)
    {
        var days = InsightsCalculator.ByDay(rows, from, to);
        return new ReportTable("Sales by day",
            [new("Day", ColumnKind.Date), new("Customers", ColumnKind.Integer), new("Sales", ColumnKind.Money), new("Profit", ColumnKind.Money)],
            days.Select(d => (object?[])[d.Day, d.Customers, d.SalesCents, d.ProfitCents]).ToList())
        {
            Notes = [$"Period: {period}", $"Total sales: R {CellText.Rand(days.Sum(d => d.SalesCents))}", $"Total profit: R {CellText.Rand(days.Sum(d => d.ProfitCents))}"],
        };
    }

    public static ReportTable SalesByProduct(IEnumerable<SaleLineRow> rows, DateTimeOffset from, DateTimeOffset to, string period)
    {
        var list = rows.Where(r => !r.Cancelled && r.SoldAt >= from && r.SoldAt <= to)
            .GroupBy(r => r.ProductId)
            .Select(g => (Name: g.OrderByDescending(r => r.SoldAt).First().Name, Units: g.Sum(r => r.Quantity),
                Revenue: g.Sum(r => r.Revenue), Profit: g.Sum(r => r.Profit)))
            .OrderByDescending(x => x.Units).ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();
        return new ReportTable("Sales by product",
            [new("Product"), new("Units sold", ColumnKind.Integer), new("Sales", ColumnKind.Money), new("Profit", ColumnKind.Money)],
            list.Select(x => (object?[])[x.Name, x.Units, x.Revenue, x.Profit]).ToList())
        { Notes = [$"Period: {period}"] };
    }

    public static ReportTable StockOnHand(IEnumerable<StockItem> items, DateTimeOffset now, DateOnly today)
    {
        var list = items.Where(i => i.IsActive).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new ReportTable("Stock on hand",
            [new("Product"), new("Barcode"), new("On hand", ColumnKind.Integer), new("Low level", ColumnKind.Integer), new("Expiry", ColumnKind.Date),
             new("Days left", ColumnKind.Integer), new("Order for 2 weeks", ColumnKind.Integer), new("Flags")],
            list.Select(i => (object?[])[i.Name, i.Barcode, i.OnHand, i.LowStockLevel, i.ExpiryDate, StockRules.DaysLeft(i),
                StockRules.SuggestedOrderQuantity(i), string.Join(" ", StockRules.Flags(i, now, today))]).ToList())
        { Notes = [$"As at {today:yyyy-MM-dd}"] };
    }

    public static ReportTable CashUps(IEnumerable<CashUpRow> rows, string period)
    {
        var list = rows.OrderBy(r => r.Day).ThenBy(r => r.TillDeviceId).ToList();
        return new ReportTable("Cash-ups",
            [new("Day", ColumnKind.Date), new("Till"), new("Float", ColumnKind.Money), new("Expected", ColumnKind.Money),
             new("Counted", ColumnKind.Money), new("Over or short", ColumnKind.Money)],
            list.Select(r => (object?[])[r.Day, r.TillDeviceId.ToString("N")[..8], r.FloatCents, r.ExpectedCents, r.CountedCents, r.DifferenceCents]).ToList())
        { Notes = [$"Period: {period}", $"Net over or short: R {CellText.Rand(list.Sum(r => r.DifferenceCents))}"] };
    }

    public static ReportTable CreditBook(IEnumerable<(Guid Id, string Name, string? Phone, long? LimitCents, long BalanceCents)> accounts, DateOnly today)
    {
        var list = accounts.OrderByDescending(a => a.BalanceCents).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new ReportTable("Credit book",
            [new("Customer"), new("Phone"), new("Credit limit", ColumnKind.Money), new("Owes", ColumnKind.Money), new("Over limit")],
            list.Select(a => (object?[])[a.Name, a.Phone, a.LimitCents, a.BalanceCents, ShopCore.Domain.CreditBook.OverLimit(a.BalanceCents, a.LimitCents) ? "Yes" : ""]).ToList())
        { Notes = [$"As at {today:yyyy-MM-dd}", $"Total owed: R {CellText.Rand(list.Sum(a => a.BalanceCents))}"] };
    }
}
