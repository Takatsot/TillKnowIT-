using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Stock;
using ShopCore.Contracts;
using ShopCore.Domain;

namespace ShopCore.Application.Tests;

public class StockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static StockItem Item(int onHand = 20, int low = 5, DateOnly? expiry = null, int sold30 = 0, DateTimeOffset? lastSold = null,
        DateTimeOffset? added = null, string name = "Bread", bool active = true) =>
        new(Guid.NewGuid(), name, "6001234567890", active, added ?? Now.AddDays(-90), onHand, low, expiry, sold30, lastSold);

    // ---- single rules ----

    [Fact]
    public void Out_means_zero_or_less_and_low_means_some_left_at_or_under_the_level()
    {
        Assert.True(StockRules.IsOut(Item(onHand: 0)));
        Assert.True(StockRules.IsOut(Item(onHand: -3)));
        Assert.False(StockRules.IsOut(Item(onHand: 1)));

        Assert.True(StockRules.IsRunningLow(Item(onHand: 5, low: 5)));
        Assert.True(StockRules.IsRunningLow(Item(onHand: 1, low: 5)));
        Assert.False(StockRules.IsRunningLow(Item(onHand: 6, low: 5)));
        Assert.False(StockRules.IsRunningLow(Item(onHand: 0, low: 5)));     // out, not low
        Assert.False(StockRules.IsRunningLow(Item(onHand: 2, low: 0)));     // level 0 = no alert
    }

    [Fact]
    public void Not_selling_needs_stock_no_recent_sale_and_a_product_older_than_thirty_days()
    {
        Assert.True(StockRules.IsNotSelling(Item(lastSold: Now.AddDays(-31)), Now));
        Assert.True(StockRules.IsNotSelling(Item(lastSold: null), Now));
        Assert.False(StockRules.IsNotSelling(Item(lastSold: Now.AddDays(-29)), Now));
        Assert.False(StockRules.IsNotSelling(Item(onHand: 0, lastSold: null), Now));
        Assert.False(StockRules.IsNotSelling(Item(lastSold: null, added: Now.AddDays(-10)), Now));
    }

    [Fact]
    public void Expiring_means_within_fourteen_days_including_already_expired_and_only_with_stock()
    {
        Assert.True(StockRules.IsExpiring(Item(expiry: Today.AddDays(14)), Today));
        Assert.True(StockRules.IsExpiring(Item(expiry: Today.AddDays(-1)), Today));
        Assert.False(StockRules.IsExpiring(Item(expiry: Today.AddDays(15)), Today));
        Assert.False(StockRules.IsExpiring(Item(expiry: null), Today));
        Assert.False(StockRules.IsExpiring(Item(onHand: 0, expiry: Today.AddDays(1)), Today));
        Assert.True(StockRules.IsExpired(Item(expiry: Today.AddDays(-1)), Today));
        Assert.False(StockRules.IsExpired(Item(expiry: Today), Today));
    }

    [Fact]
    public void Days_left_and_suggested_order_follow_the_recent_selling_rate()
    {
        // 30 sold in 30 days = 1 a day.
        var steady = Item(onHand: 10, sold30: 30);
        Assert.Equal(10, StockRules.DaysLeft(steady));
        Assert.Equal(4, StockRules.SuggestedOrderQuantity(steady));      // 14 days = 14, minus 10 on hand

        Assert.Null(StockRules.DaysLeft(Item(onHand: 10, sold30: 0)));   // nothing selling
        Assert.Null(StockRules.DaysLeft(Item(onHand: 0, sold30: 30)));   // nothing left
        Assert.Equal(14, StockRules.SuggestedOrderQuantity(Item(onHand: -5, sold30: 30)));   // negative stock counts as none
        Assert.Equal(0, StockRules.SuggestedOrderQuantity(Item(onHand: 50, sold30: 30)));    // plenty left
        Assert.Equal(0, StockRules.SuggestedOrderQuantity(Item(onHand: 0, sold30: 0)));
        Assert.Equal(5, StockRules.SuggestedOrderQuantity(Item(onHand: 0, sold30: 10)));     // 14/3 rounded up
    }

    [Fact]
    public void Negative_stock_needs_a_count()
    {
        Assert.True(StockRules.NeedsCount(Item(onHand: -1)));
        Assert.False(StockRules.NeedsCount(Item(onHand: 0)));
    }

    // ---- counts ----

    [Theory]
    [InlineData(10, 10, "counted", null, 0)]
    [InlineData(10, 12, "counted", StockMovementType.Count, 2)]
    [InlineData(10, 7, "counted", StockMovementType.Count, -3)]
    [InlineData(10, 7, "damaged", StockMovementType.WriteOff, -3)]
    [InlineData(10, 7, "expired", StockMovementType.WriteOff, -3)]
    [InlineData(10, 12, "damaged", StockMovementType.Count, 2)]
    [InlineData(-4, 0, "counted", StockMovementType.Count, 4)]
    public void A_count_becomes_the_movement_that_fixes_the_books(int onHand, int counted, string reason, StockMovementType? type, int quantity)
    {
        var result = StockCounts.FromCount(onHand, counted, reason);

        if (type is null) Assert.Null(result);
        else Assert.Equal((type.Value, quantity), result);
    }

    // ---- stock list ----

    private sealed class FakeReadStore(List<StockItem> items) : IStockReadStore
    {
        public Task<List<StockItem>> Items(Guid shopId, DateTimeOffset now, CancellationToken ct) => Task.FromResult(items);
    }

    private static Task<TillKnowIT.SharedKernel.Result<List<StockRow>>> List(List<StockItem> items, string? filter = null) =>
        new ListStockHandler(new FakeReadStore(items), new FakeTimeProvider(Now)).Handle(new ListStock(Guid.NewGuid(), filter), default);

    private static List<StockItem> Shelf() =>
    [
        Item(name: "Bread", onHand: 3, low: 5, sold30: 60, lastSold: Now.AddDays(-1)),                 // low
        Item(name: "Airtime voucher", onHand: 0, low: 5),                                              // out
        Item(name: "Old tins", onHand: 12, low: 2, lastSold: Now.AddDays(-60)),                        // not selling
        Item(name: "Milk", onHand: 20, low: 5, expiry: Today.AddDays(3), sold30: 30, lastSold: Now),   // expiring
        Item(name: "Rice", onHand: 40, low: 5, sold30: 30, lastSold: Now),                             // fine
        Item(name: "Switched off", onHand: 0, low: 5, active: false),                                  // hidden
    ];

    [Theory]
    [InlineData(null, new[] { "Airtime voucher", "Bread", "Milk", "Old tins", "Rice" })]
    [InlineData("all", new[] { "Airtime voucher", "Bread", "Milk", "Old tins", "Rice" })]
    [InlineData("low", new[] { "Bread" })]
    [InlineData("out", new[] { "Airtime voucher" })]
    [InlineData("not_selling", new[] { "Old tins" })]
    [InlineData("expiring", new[] { "Milk" })]
    [InlineData("LOW", new[] { "Bread" })]
    public async Task Each_filter_shows_the_right_products_in_name_order(string? filter, string[] expected)
    {
        var result = await List(Shelf(), filter);
        Assert.Equal(expected, result.Value!.Select(r => r.Name));
    }

    [Fact]
    public async Task Rows_carry_flags_days_left_and_a_suggested_order()
    {
        var result = await List(Shelf());
        var bread = result.Value!.Single(r => r.Name == "Bread");
        var milk = result.Value!.Single(r => r.Name == "Milk");

        Assert.Equal(["low"], bread.Flags);
        Assert.Equal(1, bread.DaysLeft);                  // 3 on hand at 2 a day
        Assert.Equal(25, bread.SuggestedOrderQuantity);   // 28 for two weeks, minus 3
        Assert.Equal(["expiring"], milk.Flags);
        Assert.Equal(20, milk.DaysLeft);
    }

    [Fact]
    public async Task Negative_stock_is_flagged_out_and_needs_count()
    {
        var result = await List([Item(name: "Mystery", onHand: -2)]);
        Assert.Equal(["out", "needs_count"], result.Value!.Single().Flags);
    }

    [Fact]
    public async Task Unknown_filter_is_rejected()
    {
        var result = await List(Shelf(), "everything");
        Assert.Equal("stock.filter_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Todays_date_is_the_south_african_business_day_not_the_utc_day()
    {
        // 23:30 UTC on the 26th is already the 27th in the shop.
        var late = new DateTimeOffset(2026, 10, 26, 23, 30, 0, TimeSpan.Zero);
        var items = new List<StockItem> { Item(name: "Yoghurt", expiry: new DateOnly(2026, 11, 10)) };

        var result = await new ListStockHandler(new FakeReadStore(items), new FakeTimeProvider(late)).Handle(new ListStock(Guid.NewGuid(), "expiring"), default);

        Assert.Single(result.Value!);   // 27 Oct + 14 days = 10 Nov, so it is expiring
    }
}
