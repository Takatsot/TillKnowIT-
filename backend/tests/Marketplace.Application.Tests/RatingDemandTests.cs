using Identity.Contracts;
using Marketplace.Application.Offers;
using Marketplace.Application.Orders;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Contracts;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Tests;

public class RatingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class Ratings : IRatingStore
    {
        public List<Rating> All { get; } = [];
        public Task<Rating?> ForOrder(Guid id, CancellationToken ct) => Task.FromResult(All.FirstOrDefault(r => r.OrderId == id));
        public Task Add(Rating r, CancellationToken ct) { All.Add(r); return Task.CompletedTask; }
        public Task<List<Rating>> ForSupplier(Guid id, CancellationToken ct) => Task.FromResult(All.Where(r => r.SupplierId == id).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Orders(PurchaseOrder order) : IOrderStore
    {
        public Task Add(PurchaseOrder o, CancellationToken ct) => Task.CompletedTask;
        public Task<PurchaseOrder?> Get(Guid id, CancellationToken ct) => Task.FromResult<PurchaseOrder?>(id == order.Id ? order : null);
        public Task<List<PurchaseOrder>> ForShop(Guid shopId, CancellationToken ct) => Task.FromResult(new List<PurchaseOrder>());
        public Task<List<PurchaseOrder>> ForSupplier(Guid id, OrderStatus? s, CancellationToken ct) => Task.FromResult(new List<PurchaseOrder>());
        public Task<LastPrice?> GetLastPrice(Guid shop, Guid supplier, string barcode, CancellationToken ct) => Task.FromResult<LastPrice?>(null);
        public Task Add(LastPrice p, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Access : IOrganisationAccess
    {
        public HashSet<(Guid, Guid)> Granted { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, o)));
    }

    private readonly Guid _owner = Guid.NewGuid();
    private readonly Ratings _ratings = new();
    private readonly Access _access = new();
    private readonly PurchaseOrder _order;

    public RatingTests()
    {
        _order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), "Coke", "6001234567890", 24, 1, 10000, true)], OrderPaymentMethod.OnDelivery, 0, 0, Now).Value!;
        _order.Send(OrderChannel.App, Now);
        _access.Granted.Add((_owner, _order.ShopId));
    }

    private void Deliver()
    {
        _order.Respond(_order.Lines.Select(l => new LineDecision(l.Id, "confirm", null, null)).ToList(), new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 26), Now);
        _order.MarkDelivered(new Dictionary<Guid, int>(), Now);
    }

    private Task<Result<RatingResponse>> Rate(int score = 4, Guid? caller = null, string? comment = null) =>
        new RateOrderHandler(new Orders(_order), _ratings, _access, new FakeTimeProvider(Now)).Handle(new RateOrder(_order.Id, new(caller ?? _owner, true, false, score, comment)), default);

    [Fact]
    public async Task A_shop_rates_a_delivered_order_once()
    {
        Deliver();

        var first = await Rate(5, comment: " Always on time ");
        var second = await Rate(1);

        Assert.Equal((5, "Always on time", true, false), (first.Value!.Score, first.Value.Comment, first.Value.OnTime, first.Value.Complete));
        Assert.Equal("rating.already_rated", second.Error!.Code);
        Assert.Single(_ratings.All);
    }

    [Fact]
    public async Task Only_the_shop_can_rate_and_only_after_delivery_with_a_score_from_1_to_5()
    {
        Assert.Equal("rating.not_delivered", (await Rate()).Error!.Code);
        Deliver();

        Assert.Equal("permission.denied", (await Rate(caller: Guid.NewGuid())).Error!.Code);
        Assert.Equal("rating.score_invalid", (await Rate(0)).Error!.Code);
        Assert.Equal("rating.score_invalid", (await Rate(6)).Error!.Code);
        Assert.Equal("rating.comment_too_long", (await Rate(comment: new string('x', 501))).Error!.Code);
        Assert.Empty(_ratings.All);
    }

    private static Rating RatingOf(int score, bool onTime, bool complete, string? comment = null)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), "Coke", null, 24, 1, 10000, true)], OrderPaymentMethod.OnDelivery, 0, 0, Now).Value!;
        order.Send(OrderChannel.App, Now);
        order.Respond(order.Lines.Select(l => new LineDecision(l.Id, "confirm", null, null)).ToList(), new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 26), Now);
        order.MarkDelivered(new Dictionary<Guid, int>(), Now);
        return Rating.Create(order, Guid.NewGuid(), onTime, complete, score, comment, Now).Value!;
    }

    [Fact]
    public void The_summary_stays_empty_until_enough_deliveries_are_rated_then_gives_average_and_percentages()
    {
        var two = RatingSummary.Of([RatingOf(5, true, true), RatingOf(5, true, true)]);
        var four = RatingSummary.Of([RatingOf(5, true, true, "Great"), RatingOf(4, true, false), RatingOf(3, false, true), RatingOf(4, true, true)]);

        Assert.Equal((2, null, null, null), (two.Count, two.AverageScore, two.OnTimePercent, two.CompletePercent));
        Assert.Equal((4, 4.0, 75, 75), (four.Count, four.AverageScore, four.OnTimePercent, four.CompletePercent));
        Assert.Equal(["Great"], four.RecentComments);
    }
}

public class DemandTests
{
    private static HashSet<string> Low(params string[] barcodes) => [.. barcodes];
    private static readonly Guid Coke = Guid.NewGuid();
    private static readonly Guid Fanta = Guid.NewGuid();
    private static readonly List<(Guid, string, string)> Products = [(Coke, "Coke 330ml", "6001"), (Fanta, "Fanta 330ml", "6002")];

    private static DemandShop Shop(string area, params string[] low) => new(area, Low(low));

    [Fact]
    public void Demand_is_a_count_per_area_and_product_and_nothing_else()
    {
        var shops = new[] { Shop("jhb", "6001"), Shop("jhb", "6001", "6002"), Shop("jhb", "6001"), Shop("jhb", "6002"), Shop("jhb", "6002") };

        var demand = DemandCalculator.Compute(shops, Products, ["jhb"], 3);

        var area = Assert.Single(demand.Areas);
        Assert.Equal(("jhb", 3, 3), (area.AreaCode, area.Products[0].ShopsLow, area.Products[1].ShopsLow));
        Assert.Equal(["Coke 330ml", "Fanta 330ml"], area.Products.Select(p => p.Name).OrderBy(n => n));
    }

    [Fact]
    public void A_count_below_the_minimum_is_never_shown_so_it_cannot_point_at_one_shop()
    {
        var shops = new[] { Shop("jhb", "6001"), Shop("jhb", "6001") };

        Assert.Empty(DemandCalculator.Compute(shops, Products, ["jhb"], 3).Areas);
    }

    [Fact]
    public void The_minimum_can_be_raised_but_never_lowered_below_the_floor()
    {
        var three = new[] { Shop("jhb", "6001"), Shop("jhb", "6001"), Shop("jhb", "6001") };

        Assert.Equal(3, DemandCalculator.Compute(three, Products, ["jhb"], 0).MinShops);
        Assert.Equal(3, DemandCalculator.Compute(three, Products, ["jhb"], 1).MinShops);
        Assert.Single(DemandCalculator.Compute(three, Products, ["jhb"], 3).Areas);
        Assert.Empty(DemandCalculator.Compute(three, Products, ["jhb"], 4).Areas);
    }

    [Fact]
    public void Only_the_suppliers_own_areas_count_and_areas_are_kept_apart()
    {
        var shops = new[] { Shop("jhb", "6001"), Shop("jhb", "6001"), Shop("cpt", "6001"), Shop("dbn", "6001"), Shop("dbn", "6001"), Shop("dbn", "6001") };

        var demand = DemandCalculator.Compute(shops, Products, ["jhb", "cpt"], 3);

        Assert.Empty(demand.Areas);   // 2 in jhb and 1 in cpt: no area on its own is enough, and durban is not served
    }

    // ---- the handler: who is counted ----

    private sealed class Suppliers(Supplier s, List<SupplierArea> areas, List<SupplierProduct> products) : ISupplierStore, ISupplierProductStore
    {
        public Task Add(Supplier x, CancellationToken ct) => Task.CompletedTask;
        public Task<Supplier?> Get(Guid id, CancellationToken ct) => Task.FromResult<Supplier?>(id == s.Id ? s : null);
        public Task<List<Supplier>> Pending(CancellationToken ct) => Task.FromResult(new List<Supplier>());
        public Task<List<SupplierArea>> Areas(Guid id, CancellationToken ct) => Task.FromResult(areas);
        public Task ReplaceAreas(Guid id, List<SupplierArea> a, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<string, SupplierProduct>> ByBarcode(Guid id, CancellationToken ct) => Task.FromResult(new Dictionary<string, SupplierProduct>());
        Task<SupplierProduct?> ISupplierProductStore.Get(Guid id, CancellationToken ct) => Task.FromResult(products.FirstOrDefault(p => p.Id == id));
        public Task<List<SupplierProduct>> List(Guid id, CancellationToken ct) => Task.FromResult(products);
        public Task Add(SupplierProduct p, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Shops(Dictionary<Guid, string> areas) : IShopDirectory
    {
        public Task<ShopInfo?> Get(Guid id, CancellationToken ct) => Task.FromResult<ShopInfo?>(new ShopInfo(id, "Secret Shop Name", "verified", null, null, null, "en", areas[id]));
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(areas.Keys.ToList());
    }

    private sealed class Sharing : IShopMarketplaceStore
    {
        public Dictionary<Guid, SharingMode> Modes { get; } = [];
        public HashSet<Guid> Usual { get; } = [];
        public Task<SharingSetting?> GetSharing(Guid shopId, CancellationToken ct) => Task.FromResult(Modes.TryGetValue(shopId, out var m) ? new SharingSetting(shopId, m) : null);
        public Task Add(SharingSetting s, CancellationToken ct) => Task.CompletedTask;
        public Task<ShopSupplier?> GetShopSupplier(Guid shop, Guid supplier, CancellationToken ct) => Task.FromResult(Usual.Contains(shop) ? new ShopSupplier(shop, supplier) : null);
        public Task Add(ShopSupplier s, CancellationToken ct) => Task.CompletedTask;
        public Task<MarketplaceSetting?> GetSetting(string key, CancellationToken ct) => Task.FromResult<MarketplaceSetting?>(null);
        public Task Add(MarketplaceSetting s, CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<string, string>> Settings(CancellationToken ct) => Task.FromResult(new Dictionary<string, string>());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Stock : IShopStockInfo
    {
        public int Asked { get; private set; }
        public Task<ShopProductInfo?> GetProduct(Guid shopId, Guid productId, CancellationToken ct) => Task.FromResult<ShopProductInfo?>(null);
        public Task<List<ShopProductInfo>> NeedingReorder(Guid shopId, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(new List<ShopProductInfo> { new(Guid.NewGuid(), "Coke", "6001234567890", 1000, 1, 5, null, null, 10, []) });
        }
    }

    private sealed class Access : IOrganisationAccess
    {
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task A_shop_that_never_chose_to_share_is_never_counted_or_even_asked_about_its_stock()
    {
        var now = new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
        var org = Guid.NewGuid();
        var supplier = Supplier.Apply(new SupplierApplication(Guid.NewGuid(), org, "Fresh Foods", null, "Sipho", "0821234567", null, null, 1), now).Value!;
        var product = SupplierProduct.Create(supplier.Id, null, new SupplierProductInput("6001234567890", "Coke 330ml", 24, 24000, true, true, null), now).Value!;
        var areas = new List<SupplierArea> { new(supplier.Id, "jhb", DeliveryDays.Mon) };
        var shopIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var sharing = new Sharing();
        var stock = new Stock();

        // two share with everyone, one shares with usual suppliers and this is one, one shares with usual suppliers but this is not one, two never chose
        sharing.Modes[shopIds[0]] = SharingMode.All;
        sharing.Modes[shopIds[1]] = SharingMode.All;
        sharing.Modes[shopIds[2]] = SharingMode.Usual;
        sharing.Usual.Add(shopIds[2]);
        sharing.Modes[shopIds[3]] = SharingMode.Usual;
        sharing.Modes[shopIds[4]] = SharingMode.Off;

        var suppliers = new Suppliers(supplier, areas, [product]);
        var handler = new GetDemandHandler(suppliers, suppliers, new Shops(shopIds.ToDictionary(id => id, _ => "jhb")), sharing, stock, new Access());

        var result = await handler.Handle(new GetDemand(supplier.Id, Guid.NewGuid()), default);

        Assert.Equal(3, Assert.Single(Assert.Single(result.Value!.Areas).Products).ShopsLow);
        Assert.Equal(3, stock.Asked);   // only the three opted-in shops had their stock looked at
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("Secret Shop Name", json);
        foreach (var id in shopIds) Assert.DoesNotContain(id.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }
}
