using Identity.Contracts;
using Marketplace.Application.Offers;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Contracts;

namespace Marketplace.Application.Tests;

public class OfferFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);   // a Monday in the shop

    // ---- fakes ----

    private sealed class FakeOfferStore : IOfferStore
    {
        public List<(OfferSource Source, string Barcode, string Municipality)> Listings { get; } = [];
        public List<Deal> Deals { get; } = [];
        public List<ShopSupplier> Usual { get; } = [];
        public Dictionary<Guid, long> Last { get; } = [];
        public Dictionary<string, string> Config { get; } = [];
        public Task<List<OfferSource>> Sources(string barcode, string municipality, CancellationToken ct) =>
            Task.FromResult(Listings.Where(l => l.Barcode == barcode && l.Municipality == municipality).Select(l => l.Source).ToList());
        public Task<List<Deal>> ActiveDeals(IEnumerable<Guid> ids, string municipality, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(Deals.Where(d => ids.Contains(d.SupplierId) && d.IsActiveAt(now) && d.AppliesToArea(municipality)).ToList());
        public Task<List<ShopSupplier>> ShopSuppliers(Guid shopId, CancellationToken ct) => Task.FromResult(Usual.Where(u => u.ShopId == shopId).ToList());
        public Task<Dictionary<Guid, long>> LastPrices(Guid shopId, string barcode, CancellationToken ct) => Task.FromResult(Last);
        public Task<Dictionary<string, string>> Settings(CancellationToken ct) => Task.FromResult(Config);
    }

    private sealed class FakeStock : IShopStockInfo
    {
        public List<ShopProductInfo> Products { get; } = [];
        public Task<ShopProductInfo?> GetProduct(Guid shopId, Guid productId, CancellationToken ct) => Task.FromResult(Products.FirstOrDefault(p => p.ProductId == productId));
        public Task<List<ShopProductInfo>> NeedingReorder(Guid shopId, CancellationToken ct) => Task.FromResult(Products);
    }

    private sealed class FakeShops : IShopDirectory
    {
        public string? Municipality { get; set; } = "jhb";
        public bool Exists { get; set; } = true;
        public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) =>
            Task.FromResult<ShopInfo?>(Exists ? new ShopInfo(shopId, "Mama's Spaza", "verified", null, null, null, "en", Municipality) : null);
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(new List<Guid>());
    }

    private sealed class FakeAccess : IOrganisationAccess, IPermissionChecker
    {
        public HashSet<(Guid, Guid, string)> InOrg { get; } = [];
        public HashSet<(Guid, string)> Global { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(InOrg.Contains((p, o, perm)));
        public Task<bool> Has(Guid p, string perm, CancellationToken ct) => Task.FromResult(Global.Contains((p, perm)));
    }

    private readonly FakeOfferStore _store = new();
    private readonly FakeStock _stock = new();
    private readonly FakeShops _shops = new();
    private readonly FakeAccess _access = new();
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private const string Barcode = "6001234567890";

    public OfferFlowTests()
    {
        _access.InOrg.Add((_owner, _shop, "stock.receive"));
        _access.InOrg.Add((_owner, _shop, "shop.settings"));
    }

    private OfferHandlers Offers() => new(_store, _stock, _shops, _access, new FakeTimeProvider(Now));

    private ShopProductInfo Product(string name = "Coke 330ml", string? barcode = Barcode, long price = 1500, int suggested = 48) =>
        new(Guid.NewGuid(), name, barcode, price, 3, 10, null, 2, suggested, ["low"]);

    private OfferSource Listing(string supplier, long casePrice = 24000, int perCase = 24, bool vat = true, long delivery = 0, long min = 0,
        DeliveryDays days = DeliveryDays.Tue | DeliveryDays.Thu, string municipality = "jhb", string barcode = Barcode, double? rating = null)
    {
        var source = new OfferSource(Guid.NewGuid(), supplier, min, delivery, Guid.NewGuid(), perCase, casePrice, vat, days, rating);
        _store.Listings.Add((source, barcode, municipality));
        return source;
    }

    // ---- offers for one product ----

    [Fact]
    public async Task Offers_are_ranked_by_true_cost_with_the_margin_at_the_shops_price()
    {
        var product = Product(price: 1500, suggested: 48);
        _stock.Products.Add(product);
        Listing("Dear", casePrice: 27000);
        Listing("Cheap", casePrice: 22000);
        Listing("Wrong area", casePrice: 10000, municipality: "cpt");

        var result = (await Offers().Handle(new GetOffers(_shop, _owner, product.ProductId), default)).Value!;

        Assert.Equal(["Cheap", "Dear"], result.Offers.Select(o => o.SupplierName));
        Assert.Equal(48, result.QuantityUnits);
        Assert.Equal(1500, result.SellingPriceCents);
        Assert.Equal((917L, true, 2), (result.Offers[0].TrueCostPerUnitCents, result.Offers[0].IsCheapest, result.Offers[0].Cases));
        Assert.Equal(38.9, result.Offers[0].MarginPercent);
    }

    [Fact]
    public async Task A_running_deal_lowers_the_cost_and_a_sponsored_one_is_labelled()
    {
        var product = Product(suggested: 24);
        _stock.Products.Add(product);
        var plain = Listing("Plain", casePrice: 24000);
        var promo = Listing("Promo", casePrice: 24000);
        var terms = new DealTerms(SupplierProductId: promo.SupplierProductId, PercentOff: 20);
        _store.Deals.Add(Deal.Create(promo.SupplierId, new DealInput("20% off Coke", DealType.Discount, terms, Now.AddDays(-1), Now.AddDays(10), ["jhb"], IsSponsored: true), ["jhb"], Now.AddDays(-2)).Value!);

        var offers = (await Offers().Handle(new GetOffers(_shop, _owner, product.ProductId), default)).Value!.Offers;

        Assert.Equal(["Promo", "Plain"], offers.Select(o => o.SupplierName));
        Assert.Equal((800L, "20% off Coke", "Sponsored"), (offers[0].TrueCostPerUnitCents, offers[0].DealTitle, offers[0].Label));
        Assert.Null(offers[1].Label);
        _ = plain;
    }

    [Fact]
    public async Task A_deal_that_has_ended_or_is_for_another_area_does_not_apply()
    {
        var product = Product(suggested: 24);
        _stock.Products.Add(product);
        var supplier = Listing("Promo", casePrice: 24000);
        var terms = new DealTerms(SupplierProductId: supplier.SupplierProductId, PercentOff: 50);
        _store.Deals.Add(Deal.Create(supplier.SupplierId, new DealInput("Old", DealType.Discount, terms, Now.AddDays(-30), Now.AddDays(-1), ["jhb"], false), ["jhb"], Now.AddDays(-31)).Value!);
        _store.Deals.Add(Deal.Create(supplier.SupplierId, new DealInput("Elsewhere", DealType.Discount, terms, Now.AddDays(-1), Now.AddDays(5), ["cpt"], false), ["cpt"], Now.AddDays(-2)).Value!);

        var offer = (await Offers().Handle(new GetOffers(_shop, _owner, product.ProductId), default)).Value!.Offers.Single();

        Assert.Equal(1000, offer.TrueCostPerUnitCents);
        Assert.Null(offer.DealTitle);
    }

    [Fact]
    public async Task The_usual_supplier_comes_first_with_the_price_the_shop_last_paid()
    {
        var product = Product();
        _stock.Products.Add(product);
        Listing("Cheap", casePrice: 20000);
        var mine = Listing("My usual", casePrice: 26000);
        _store.Usual.Add(new ShopSupplier(_shop, mine.SupplierId));
        _store.Last[mine.SupplierId] = 25000;

        var offers = (await Offers().Handle(new GetOffers(_shop, _owner, product.ProductId), default)).Value!.Offers;

        Assert.Equal(["My usual", "Cheap"], offers.Select(o => o.SupplierName));
        Assert.Equal((true, 25000L, false), (offers[0].IsUsual, offers[0].LastPriceCents, offers[0].IsCheapest));
        Assert.True(offers[1].IsCheapest);
    }

    [Fact]
    public async Task Delivery_days_away_come_from_the_suppliers_delivery_days_counting_from_tomorrow()
    {
        var product = Product();
        _stock.Products.Add(product);
        Listing("Tue and Thu", days: DeliveryDays.Tue | DeliveryDays.Thu);          // today is Monday: Tuesday is 1 day away
        Listing("Mondays only", days: DeliveryDays.Mon, casePrice: 24001);           // an order today cannot arrive today: next Monday is 7 away

        var offers = (await Offers().Handle(new GetOffers(_shop, _owner, product.ProductId), default)).Value!.Offers;

        Assert.Equal([1, 7], offers.OrderBy(o => o.SupplierName == "Tue and Thu" ? 0 : 1).Select(o => o.DeliveryDaysAway));
    }

    [Fact]
    public async Task The_platforms_ranking_settings_change_the_order_without_a_release()
    {
        var product = Product(suggested: 24);
        _stock.Products.Add(product);
        Listing("Five stars", casePrice: 24500, rating: 5);
        Listing("Cheaper, unrated", casePrice: 24000);

        Assert.Equal("Cheaper, unrated", (await Offers().Handle(new GetOffers(_shop, _owner, product.ProductId), default)).Value!.Offers[0].SupplierName);

        _store.Config["ranking.weight_rating"] = "50";
        Assert.Equal("Five stars", (await Offers().Handle(new GetOffers(_shop, _owner, product.ProductId), default)).Value!.Offers[0].SupplierName);
    }

    [Fact]
    public async Task A_product_without_a_barcode_has_no_offers_and_at_least_one_unit_is_always_priced()
    {
        var noBarcode = Product(barcode: null);
        var noNeed = Product("Rice", suggested: 0);
        _stock.Products.AddRange([noBarcode, noNeed]);
        Listing("Rice seller", casePrice: 10000, perCase: 10, barcode: Barcode);

        Assert.Empty((await Offers().Handle(new GetOffers(_shop, _owner, noBarcode.ProductId), default)).Value!.Offers);
        var rice = (await Offers().Handle(new GetOffers(_shop, _owner, noNeed.ProductId), default)).Value!;
        Assert.Equal(1, rice.QuantityUnits);
        Assert.Equal(1, rice.Offers.Single().Cases);
    }

    [Fact]
    public async Task Offers_need_access_to_the_shop_a_municipality_and_a_real_product()
    {
        var product = Product();
        _stock.Products.Add(product);

        Assert.Equal("permission.denied", (await Offers().Handle(new GetOffers(_shop, Guid.NewGuid(), product.ProductId), default)).Error!.Code);
        Assert.Equal("product.not_found", (await Offers().Handle(new GetOffers(_shop, _owner, Guid.NewGuid()), default)).Error!.Code);

        _shops.Municipality = null;
        Assert.Equal("offers.no_municipality", (await Offers().Handle(new GetOffers(_shop, _owner, product.ProductId), default)).Error!.Code);
        _shops.Municipality = "jhb";
        _shops.Exists = false;
        Assert.Equal("shop.not_found", (await Offers().Handle(new GetOffers(_shop, _owner, product.ProductId), default)).Error!.Code);
    }

    // ---- order list ----

    [Fact]
    public async Task The_order_list_shows_each_product_that_needs_reordering_with_its_best_and_cheapest_option()
    {
        var coke = Product("Coke 330ml");
        var rice = Product("Rice 2kg", barcode: "6009999999999", suggested: 10);
        var orphan = Product("Unlisted sweets", barcode: "6007777777777");
        _stock.Products.AddRange([coke, rice, orphan]);
        var mine = Listing("My usual", casePrice: 26000);
        Listing("Cheap", casePrice: 20000);
        Listing("Rice seller", casePrice: 5000, perCase: 5, barcode: "6009999999999");
        _store.Usual.Add(new ShopSupplier(_shop, mine.SupplierId));

        var list = (await Offers().Handle(new GetOrderList(_shop, _owner), default)).Value!;

        var cokeLine = list.Single(i => i.ProductName == "Coke 330ml");
        Assert.Equal((2, "My usual", "Cheap"), (cokeLine.OfferCount, cokeLine.Best!.SupplierName, cokeLine.Cheapest!.SupplierName));
        Assert.Equal(["low"], cokeLine.Flags);
        Assert.Equal(1, list.Single(i => i.ProductName == "Rice 2kg").OfferCount);
        var none = list.Single(i => i.ProductName == "Unlisted sweets");
        Assert.Equal(0, none.OfferCount);
        Assert.Null(none.Best);
        Assert.Null(none.Cheapest);
    }

    [Fact]
    public async Task The_order_list_needs_access_to_the_shop_and_its_municipality()
    {
        Assert.Equal("permission.denied", (await Offers().Handle(new GetOrderList(_shop, Guid.NewGuid()), default)).Error!.Code);
        _shops.Municipality = null;
        Assert.Equal("offers.no_municipality", (await Offers().Handle(new GetOrderList(_shop, _owner), default)).Error!.Code);
    }

    // ---- sharing ----

    private SharingHandlers Sharing(FakeShopMarketplace store) => new(store, _access);

    private sealed class FakeShopMarketplace : IShopMarketplaceStore
    {
        public List<SharingSetting> Sharing { get; } = [];
        public List<ShopSupplier> ShopSuppliers { get; } = [];
        public List<MarketplaceSetting> SettingsRows { get; } = [];
        public Task<SharingSetting?> GetSharing(Guid shopId, CancellationToken ct) => Task.FromResult(Sharing.FirstOrDefault(s => s.ShopId == shopId));
        public Task Add(SharingSetting s, CancellationToken ct) { Sharing.Add(s); return Task.CompletedTask; }
        public Task<ShopSupplier?> GetShopSupplier(Guid shopId, Guid supplierId, CancellationToken ct) => Task.FromResult(ShopSuppliers.FirstOrDefault(s => s.ShopId == shopId && s.SupplierId == supplierId));
        public Task Add(ShopSupplier s, CancellationToken ct) { ShopSuppliers.Add(s); return Task.CompletedTask; }
        public Task<MarketplaceSetting?> GetSetting(string key, CancellationToken ct) => Task.FromResult(SettingsRows.FirstOrDefault(s => s.Key == key));
        public Task Add(MarketplaceSetting s, CancellationToken ct) { SettingsRows.Add(s); return Task.CompletedTask; }
        public Task<Dictionary<string, string>> Settings(CancellationToken ct) => Task.FromResult(SettingsRows.ToDictionary(s => s.Key, s => s.Value));
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Sharing_is_off_until_the_owner_chooses_and_only_the_owner_can_choose()
    {
        var store = new FakeShopMarketplace();

        Assert.Equal("off", (await Sharing(store).Handle(new GetSharing(_shop, _owner), default)).Value!.Mode);
        Assert.Equal("usual", (await Sharing(store).Handle(new SetSharing(_shop, new(_owner, "Usual")), default)).Value!.Mode);
        Assert.Equal("all", (await Sharing(store).Handle(new SetSharing(_shop, new(_owner, "all")), default)).Value!.Mode);
        Assert.Single(store.Sharing);                                                             // changed in place, not duplicated
        Assert.Equal("off", (await Sharing(store).Handle(new SetSharing(_shop, new(_owner, "off")), default)).Value!.Mode);

        Assert.Equal("sharing.mode_invalid", (await Sharing(store).Handle(new SetSharing(_shop, new(_owner, "everyone")), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Sharing(store).Handle(new SetSharing(_shop, new(Guid.NewGuid(), "all")), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Sharing(store).Handle(new GetSharing(_shop, Guid.NewGuid()), default)).Error!.Code);
    }

    // ---- usual supplier ----

    private sealed class FakeSupplierStore : ISupplierStore
    {
        public Dictionary<Guid, Supplier> Suppliers { get; } = [];
        public Task Add(Supplier s, CancellationToken ct) { Suppliers[s.Id] = s; return Task.CompletedTask; }
        public Task<Supplier?> Get(Guid id, CancellationToken ct) => Task.FromResult(Suppliers.GetValueOrDefault(id));
        public Task<List<Supplier>> Pending(CancellationToken ct) => Task.FromResult(new List<Supplier>());
        public Task<List<SupplierArea>> Areas(Guid id, CancellationToken ct) => Task.FromResult(new List<SupplierArea>());
        public Task ReplaceAreas(Guid id, List<SupplierArea> a, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task The_owner_can_make_a_trading_supplier_or_one_they_added_themselves_their_usual_one_but_not_someone_elses()
    {
        var marketplace = new FakeShopMarketplace();
        var suppliers = new FakeSupplierStore();
        var trading = Supplier.Apply(new SupplierApplication(_owner, Guid.NewGuid(), "Fresh Foods", null, "Sipho", "0821234567", null, null, 1), Now).Value!;
        trading.Verify(Guid.NewGuid(), SupplierVerification.Items, Now);
        var pending = Supplier.Apply(new SupplierApplication(_owner, Guid.NewGuid(), "Not yet", null, "Sipho", "0821234567", null, null, 1), Now).Value!;
        var mine = Supplier.Unlisted(_shop, "Cash and carry", "0825550000", null, null, Now).Value!;
        var theirs = Supplier.Unlisted(Guid.NewGuid(), "Someone else's", "0825551111", null, null, Now).Value!;
        foreach (var s in new[] { trading, pending, mine, theirs }) suppliers.Suppliers[s.Id] = s;
        var handler = new SetUsualSupplierHandler(marketplace, suppliers, _access);

        Assert.True((await handler.Handle(new SetUsualSupplier(_shop, new(_owner, trading.Id)), default)).IsSuccess);
        Assert.True((await handler.Handle(new SetUsualSupplier(_shop, new(_owner, trading.Id)), default)).IsSuccess);     // again: no duplicate
        Assert.True((await handler.Handle(new SetUsualSupplier(_shop, new(_owner, mine.Id)), default)).IsSuccess);
        Assert.Equal(2, marketplace.ShopSuppliers.Count);

        Assert.Equal("supplier.not_found", (await handler.Handle(new SetUsualSupplier(_shop, new(_owner, pending.Id)), default)).Error!.Code);
        Assert.Equal("supplier.not_found", (await handler.Handle(new SetUsualSupplier(_shop, new(_owner, theirs.Id)), default)).Error!.Code);
        Assert.Equal("permission.denied", (await handler.Handle(new SetUsualSupplier(_shop, new(Guid.NewGuid(), trading.Id)), default)).Error!.Code);
    }

    // ---- settings ----

    [Fact]
    public async Task Only_a_platform_admin_can_change_ranking_settings_and_only_with_allowed_values()
    {
        var admin = Guid.NewGuid();
        _access.Global.Add((admin, "marketplace.settings"));
        var store = new FakeShopMarketplace();
        var handler = new MarketplaceSettingsHandlers(store, _access);

        Assert.True((await handler.Handle(new SaveMarketplaceSetting("ranking.weight_rating", new(admin, "2.5")), default)).IsSuccess);
        Assert.True((await handler.Handle(new SaveMarketplaceSetting("ranking.weight_rating", new(admin, "3")), default)).IsSuccess);
        Assert.Single(store.SettingsRows);
        Assert.Equal("3", (await handler.Handle(new GetMarketplaceSettings(admin), default)).Value!["ranking.weight_rating"]);

        Assert.Equal("setting.key_invalid", (await handler.Handle(new SaveMarketplaceSetting("ranking.secret", new(admin, "1")), default)).Error!.Code);
        Assert.Equal("setting.value_invalid", (await handler.Handle(new SaveMarketplaceSetting("ranking.weight_cost", new(admin, "-1")), default)).Error!.Code);
        Assert.Equal("setting.value_invalid", (await handler.Handle(new SaveMarketplaceSetting("ranking.sponsored_boost_percent", new(admin, "50")), default)).Error!.Code);
        Assert.Equal("setting.value_invalid", (await handler.Handle(new SaveMarketplaceSetting("ranking.vat_percent", new(admin, "lots")), default)).Error!.Code);
        Assert.Equal("permission.denied", (await handler.Handle(new SaveMarketplaceSetting("ranking.weight_cost", new(Guid.NewGuid(), "1")), default)).Error!.Code);
        Assert.Equal("permission.denied", (await handler.Handle(new GetMarketplaceSettings(Guid.NewGuid()), default)).Error!.Code);
    }
}
