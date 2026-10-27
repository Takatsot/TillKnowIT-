using ShopCore.Application.Stock;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Insights;

public interface IInsightsReadStore
{
    /// <summary>One row per sale line in the window, with whether its sale was cancelled.</summary>
    Task<List<SaleLineRow>> SaleLines(Guid shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>E8-1 and E8-2: today, 7 and 30 days, best sellers, and what to reorder.</summary>
public sealed record GetInsights(Guid ShopId) : IQuery<InsightsResponse>;

internal sealed class GetInsightsHandler(IInsightsReadStore sales, IStockReadStore stock, TimeProvider clock)
    : IQueryHandler<GetInsights, InsightsResponse>
{
    private const int ListSize = 10;

    public async Task<Result<InsightsResponse>> Handle(GetInsights query, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await sales.SaleLines(query.ShopId, InsightsCalculator.PeriodStart(now, 30), now, ct);
        var today = BusinessDay.Of(now);

        PeriodSummaryResponse Period(int days)
        {
            var s = InsightsCalculator.Summarise(rows, now, days);
            return new PeriodSummaryResponse(s.Days, s.SalesCents, s.ProfitCents, s.Customers, s.AverageSaleCents);
        }

        var items = (await stock.Items(query.ShopId, now, ct)).Where(i => i.IsActive).ToList();

        List<StockRow> Pick(Func<StockItem, bool> filter, Func<StockItem, object> order) => items.Where(filter).OrderBy(order)
            .Take(ListSize)
            .Select(i => new StockRow(i.ProductId, i.Name, i.Barcode, i.OnHand, i.LowStockLevel, i.ExpiryDate, StockRules.DaysLeft(i),
                StockRules.SuggestedOrderQuantity(i), StockRules.Flags(i, now, today)))
            .ToList();

        return Result<InsightsResponse>.Ok(new InsightsResponse(
            Period(1), Period(7), Period(30),
            InsightsCalculator.BestSellers(rows, now, 30, ListSize).Select(b => new BestSellerResponse(b.ProductId, b.Name, b.Units, b.RevenueCents, b.ProfitCents)).ToList(),
            // Running low and out of stock first, soonest to run out at the top.
            Pick(i => StockRules.IsRunningLow(i) || (StockRules.IsOut(i) && StockRules.DailySalesRate(i) > 0), i => StockRules.DaysLeft(i) ?? -1),
            Pick(i => StockRules.IsNotSelling(i, now), i => i.LastSoldAt ?? DateTimeOffset.MinValue),
            Pick(i => StockRules.IsExpiring(i, today), i => i.ExpiryDate ?? DateOnly.MaxValue)));
    }
}
