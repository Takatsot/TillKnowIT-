using ShopCore.Contracts;
using ShopCore.Domain;

namespace ShopCore.Application.Stock;

/// <summary>The marketplace's window onto a shop's stock: price, how much is left, and how much to reorder.</summary>
public sealed class ShopStockInfo(IStockReadStore store, TimeProvider clock) : IShopStockInfo
{
    public async Task<ShopProductInfo?> GetProduct(Guid shopId, Guid productId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var item = (await store.Items(shopId, now, ct)).FirstOrDefault(i => i.ProductId == productId && i.IsActive);
        return item is null ? null : Map(item, now);
    }

    public async Task<List<ShopProductInfo>> NeedingReorder(Guid shopId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = BusinessDay.Of(now);
        return (await store.Items(shopId, now, ct))
            .Where(i => i.IsActive && (StockRules.IsRunningLow(i) || (StockRules.IsOut(i) && StockRules.DailySalesRate(i) > 0) || StockRules.IsExpiring(i, today)))
            .OrderBy(i => StockRules.DaysLeft(i) ?? -1).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(i => Map(i, now)).ToList();
    }

    private static ShopProductInfo Map(StockItem i, DateTimeOffset now) => new(
        i.ProductId, i.Name, i.Barcode, i.PriceCents, i.OnHand, i.LowStockLevel, i.ExpiryDate, StockRules.DaysLeft(i),
        StockRules.SuggestedOrderQuantity(i), StockRules.Flags(i, now, BusinessDay.Of(now)));
}
