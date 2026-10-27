using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Stock;

public interface IStockReadStore
{
    /// <summary>Every active product of the shop with its stock worked out from the movements.</summary>
    Task<List<StockItem>> Items(Guid shopId, DateTimeOffset now, CancellationToken ct);
}

/// <summary>E5-1: the stock list with the filters running low, out, not selling and expiring.</summary>
public sealed record ListStock(Guid ShopId, string? Filter) : IQuery<List<StockRow>>;

internal sealed class ListStockHandler(IStockReadStore store, TimeProvider clock) : IQueryHandler<ListStock, List<StockRow>>
{
    public async Task<Result<List<StockRow>>> Handle(ListStock query, CancellationToken ct)
    {
        if (!TryParse(query.Filter, out var filter))
            return Result<List<StockRow>>.Fail("stock.filter_invalid", "Filter must be all, low, out, not_selling or expiring.");

        var now = clock.GetUtcNow();
        // Dates in the shop are South African dates, so "today" is the business day, not the UTC day.
        var today = BusinessDay.Of(now);

        var rows = (await store.Items(query.ShopId, now, ct))
            .Where(i => i.IsActive && StockRules.Matches(i, filter, now, today))
            .Select(i => new StockRow(i.ProductId, i.Name, i.Barcode, i.OnHand, i.LowStockLevel, i.ExpiryDate,
                StockRules.DaysLeft(i), StockRules.SuggestedOrderQuantity(i), StockRules.Flags(i, now, today)))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return Result<List<StockRow>>.Ok(rows);
    }

    private static bool TryParse(string? value, out StockFilter filter)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "all": filter = StockFilter.All; return true;
            case "low": filter = StockFilter.RunningLow; return true;
            case "out": filter = StockFilter.Out; return true;
            case "not_selling": filter = StockFilter.NotSelling; return true;
            case "expiring": filter = StockFilter.Expiring; return true;
            default: filter = default; return false;
        }
    }
}
