using Identity.Contracts;
using Marketplace.Application.Offers;
using Marketplace.Application.Orders;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using Messaging.Contracts;
using Microsoft.Extensions.Time.Testing;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Tests;

public class GroupDealDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly string[] Areas = ["jhb", "cpt"];

    private static GroupDeal Deal(int min = 10) => GroupDeal.Create(Guid.NewGuid(), new(Guid.NewGuid(), 22000, min, Now.AddDays(7), "jhb"), Areas, Now).Value!;

    [Fact]
    public void A_group_needs_a_price_a_sensible_minimum_a_future_closing_within_60_days_and_an_area_the_supplier_serves()
    {
        string Code(long price = 22000, int min = 10, int days = 7, string area = "jhb") =>
            GroupDeal.Create(Guid.NewGuid(), new(Guid.NewGuid(), price, min, Now.AddDays(days), area), Areas, Now).Error!.Code;

        Assert.Equal("group.price_invalid", Code(price: 0));
        Assert.Equal("group.minimum_invalid", Code(min: 1));
        Assert.Equal("group.closing_invalid", Code(days: -1));
        Assert.Equal("group.closing_invalid", Code(days: 61));
        Assert.Equal("group.area_invalid", Code(area: "dbn"));
        Assert.Equal(GroupDealStatus.Open, Deal().Status);
    }

    [Fact]
    public void A_shop_joins_changes_its_quantity_and_withdraws_with_zero_and_the_total_follows()
    {
        var deal = Deal();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        deal.Commit(a, Guid.NewGuid(), 4, Now);
        deal.Commit(b, Guid.NewGuid(), 3, Now);
        deal.Commit(a, Guid.NewGuid(), 6, Now);
        Assert.Equal((9, 6, 3), (deal.TotalCases, deal.CasesOf(a), deal.CasesOf(b)));

        deal.Commit(b, Guid.NewGuid(), 0, Now);
        Assert.Equal((6, 0), (deal.TotalCases, deal.CasesOf(b)));
        Assert.Single(deal.Commitments);
    }

    [Fact]
    public void Quantities_are_bounded_and_nobody_can_join_after_closing_time_or_once_closed()
    {
        var deal = Deal();

        Assert.Equal("group.quantity_invalid", deal.Commit(Guid.NewGuid(), Guid.NewGuid(), -1, Now).Error!.Code);
        Assert.Equal("group.quantity_invalid", deal.Commit(Guid.NewGuid(), Guid.NewGuid(), 10_001, Now).Error!.Code);
        Assert.Equal("group.closed", deal.Commit(Guid.NewGuid(), Guid.NewGuid(), 1, Now.AddDays(7)).Error!.Code);

        deal.Cancel();
        Assert.Equal("group.closed", deal.Commit(Guid.NewGuid(), Guid.NewGuid(), 1, Now).Error!.Code);
    }

    [Fact]
    public void At_the_deadline_the_total_decides_reached_or_failed_and_not_before()
    {
        var reached = Deal(10);
        reached.Commit(Guid.NewGuid(), Guid.NewGuid(), 6, Now);
        reached.Commit(Guid.NewGuid(), Guid.NewGuid(), 4, Now);
        var failed = Deal(10);
        failed.Commit(Guid.NewGuid(), Guid.NewGuid(), 9, Now);

        Assert.Equal("group.not_due", reached.CloseDue(Now.AddDays(6)).Error!.Code);
        reached.CloseDue(Now.AddDays(7));
        failed.CloseDue(Now.AddDays(7));

        Assert.Equal((GroupDealStatus.Reached, GroupDealStatus.Failed), (reached.Status, failed.Status));
        Assert.Equal("group.not_open", reached.CloseDue(Now.AddDays(8)).Error!.Code);
    }

    [Fact]
    public void The_supplier_can_close_early_only_once_the_minimum_is_met()
    {
        var deal = Deal(10);
        deal.Commit(Guid.NewGuid(), Guid.NewGuid(), 9, Now);
        Assert.Equal("group.minimum_not_met", deal.CloseEarly().Error!.Code);

        deal.Commit(Guid.NewGuid(), Guid.NewGuid(), 1, Now);
        Assert.True(deal.CloseEarly().IsSuccess);
        Assert.Equal(GroupDealStatus.Reached, deal.Status);
    }
}

public class GroupDealFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeGroups : IGroupDealStore
    {
        public List<GroupDeal> Deals { get; } = [];
        public Task Add(GroupDeal d, CancellationToken ct) { Deals.Add(d); return Task.CompletedTask; }
        public Task<GroupDeal?> Get(Guid id, CancellationToken ct) => Task.FromResult(Deals.FirstOrDefault(d => d.Id == id));
        public Task<List<GroupDeal>> OpenInArea(string area, CancellationToken ct) => Task.FromResult(Deals.Where(d => d.AreaCode == area && d.Status == GroupDealStatus.Open).ToList());
        public Task<List<GroupDeal>> ForSupplier(Guid id, CancellationToken ct) => Task.FromResult(Deals.Where(d => d.SupplierId == id).ToList());
        public Task<List<GroupDeal>> NeedingClosure(DateTimeOffset now, int take, CancellationToken ct) => Task.FromResult(Deals.Where(d =>
            (d.Status == GroupDealStatus.Open && d.ClosesAt <= now) || (d.Status == GroupDealStatus.Reached && d.Commitments.Any(c => c.OrderId is null))).Take(take).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeSuppliers : ISupplierStore, ISupplierProductStore
    {
        public Dictionary<Guid, Supplier> Suppliers { get; } = [];
        public List<SupplierProduct> Products { get; } = [];
        public List<SupplierArea> AreaList { get; } = [];
        public Task Add(Supplier s, CancellationToken ct) { Suppliers[s.Id] = s; return Task.CompletedTask; }
        public Task<Supplier?> Get(Guid id, CancellationToken ct) => Task.FromResult(Suppliers.GetValueOrDefault(id));
        public Task<List<Supplier>> Pending(CancellationToken ct) => Task.FromResult(new List<Supplier>());
        public Task<List<SupplierArea>> Areas(Guid id, CancellationToken ct) => Task.FromResult(AreaList.Where(a => a.SupplierId == id).ToList());
        public Task ReplaceAreas(Guid id, List<SupplierArea> a, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<string, SupplierProduct>> ByBarcode(Guid id, CancellationToken ct) => Task.FromResult(new Dictionary<string, SupplierProduct>());
        Task<SupplierProduct?> ISupplierProductStore.Get(Guid id, CancellationToken ct) => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));
        public Task<List<SupplierProduct>> List(Guid id, CancellationToken ct) => Task.FromResult(Products.Where(p => p.SupplierId == id).ToList());
        public Task Add(SupplierProduct p, CancellationToken ct) { Products.Add(p); return Task.CompletedTask; }
    }

    private sealed class FakeOrders : IOrderStore
    {
        public List<PurchaseOrder> Orders { get; } = [];
        public Task Add(PurchaseOrder o, CancellationToken ct) { Orders.Add(o); return Task.CompletedTask; }
        public Task<PurchaseOrder?> Get(Guid id, CancellationToken ct) => Task.FromResult(Orders.FirstOrDefault(o => o.Id == id));
        public Task<List<PurchaseOrder>> ForShop(Guid shopId, CancellationToken ct) => Task.FromResult(Orders.Where(o => o.ShopId == shopId).ToList());
        public Task<List<PurchaseOrder>> ForSupplier(Guid id, OrderStatus? s, CancellationToken ct) => Task.FromResult(Orders.Where(o => o.SupplierId == id).ToList());
        public Task<LastPrice?> GetLastPrice(Guid shop, Guid supplier, string barcode, CancellationToken ct) => Task.FromResult<LastPrice?>(null);
        public Task Add(LastPrice p, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeShops : IShopDirectory
    {
        public Dictionary<Guid, string> Areas { get; } = [];
        public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) =>
            Task.FromResult<ShopInfo?>(Areas.TryGetValue(shopId, out var area) ? new ShopInfo(shopId, "Shop " + shopId.ToString("N")[..4], "verified", null, "0821110000", null, "en", area) : null);
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(Areas.Keys.ToList());
    }

    private sealed class FakeQueue : IMessageQueue
    {
        public List<QueueMessageRequest> Sent { get; } = [];
        public Task<Result<QueueMessageResult>> Queue(QueueMessageRequest r, CancellationToken ct)
        {
            if (Sent.Any(m => m.DedupeKey == r.DedupeKey)) return Task.FromResult(Result<QueueMessageResult>.Ok(new QueueMessageResult(Guid.NewGuid(), true)));
            Sent.Add(r);
            return Task.FromResult(Result<QueueMessageResult>.Ok(new QueueMessageResult(Guid.NewGuid(), false)));
        }
    }

    private sealed class FakeAccess : IOrganisationAccess
    {
        public HashSet<(Guid, Guid, string)> Granted { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, o, perm)));
    }

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FakeGroups _groups = new();
    private readonly FakeSuppliers _suppliers = new();
    private readonly FakeOrders _orders = new();
    private readonly FakeShops _shops = new();
    private readonly FakeQueue _queue = new();
    private readonly FakeAccess _access = new();
    private readonly Guid _supplierPerson = Guid.NewGuid();
    private readonly Guid _supplierOrg = Guid.NewGuid();
    private readonly Supplier _supplier;
    private readonly SupplierProduct _coke;

    public GroupDealFlowTests()
    {
        _supplier = Supplier.Apply(new SupplierApplication(_supplierPerson, _supplierOrg, "Fresh Foods", null, "Sipho", "0821234567", "0825550000", "orders@fresh.co.za", 1), Start).Value!;
        _supplier.Verify(Guid.NewGuid(), SupplierVerification.Items, Start);
        _supplier.SetTerms(0, 5000, PaymentTerms.Both);
        _suppliers.Suppliers[_supplier.Id] = _supplier;
        _suppliers.AreaList.Add(new SupplierArea(_supplier.Id, "jhb", DeliveryDays.Mon));
        _suppliers.AreaList.Add(new SupplierArea(_supplier.Id, "cpt", DeliveryDays.Mon));
        _coke = SupplierProduct.Create(_supplier.Id, null, new SupplierProductInput("6001111111111", "Coke 330ml", 24, 24000, true, true, null), Start).Value!;
        _suppliers.Products.Add(_coke);
        _access.Granted.Add((_supplierPerson, _supplierOrg, "deals.manage"));
    }

    private OrderNotifier Notifier() => new(_shops, _queue);
    private GroupDealClosing Closing() => new(_groups, _suppliers, _suppliers, _orders, Notifier(), _clock);
    private PostGroupDealHandler Poster() => new(_suppliers, _suppliers, _groups, _access, _clock);
    private JoinGroupDealHandler Joiner() => new(_groups, _suppliers, _suppliers, _shops, _access, _clock);
    private CloseDueGroupDealsHandler Job() => new(_groups, Closing(), _clock);
    private ManageGroupDealHandlers Manager() => new(_suppliers, _groups, _access, Closing());
    private GroupDealQueries Queries() => new(_groups, _suppliers, _suppliers, _shops, _access);

    private async Task<GroupDealResponse> Posted(int min = 10, string area = "jhb") =>
        (await Poster().Handle(new PostGroupDeal(_supplier.Id, new(_supplierPerson, _coke.Id, 22000, min, Start.AddDays(7), area)), default)).Value!;

    private (Guid Shop, Guid Owner) NewShop(string area = "jhb")
    {
        var shop = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _shops.Areas[shop] = area;
        _access.Granted.Add((owner, shop, "stock.receive"));
        return (shop, owner);
    }

    private Task<Result<GroupDealResponse>> Join(GroupDealResponse deal, (Guid Shop, Guid Owner) s, int cases) =>
        Joiner().Handle(new JoinGroupDeal(deal.Id, s.Shop, new(s.Owner, cases)), default);

    // ---- posting ----

    [Fact]
    public async Task A_verified_supplier_posts_a_group_deal_for_its_own_product_in_an_area_it_serves()
    {
        var deal = await Posted();

        Assert.Equal(("open", "Coke 330ml", 22000L, 0, 0), (deal.Status, deal.ProductName, deal.BulkPriceCents, deal.TotalCases, deal.ProgressPercent));
        Assert.Equal(0, deal.Shops);
    }

    [Fact]
    public async Task Posting_needs_the_suppliers_permission_a_verified_supplier_and_its_own_product()
    {
        Assert.Equal("permission.denied", (await Poster().Handle(new PostGroupDeal(_supplier.Id, new(Guid.NewGuid(), _coke.Id, 22000, 10, Start.AddDays(7), "jhb")), default)).Error!.Code);
        Assert.Equal("group.product_unknown", (await Poster().Handle(new PostGroupDeal(_supplier.Id, new(_supplierPerson, Guid.NewGuid(), 22000, 10, Start.AddDays(7), "jhb")), default)).Error!.Code);
        Assert.Equal("group.area_invalid", (await Poster().Handle(new PostGroupDeal(_supplier.Id, new(_supplierPerson, _coke.Id, 22000, 10, Start.AddDays(7), "dbn")), default)).Error!.Code);
        Assert.Equal("supplier.not_found", (await Poster().Handle(new PostGroupDeal(Guid.NewGuid(), new(_supplierPerson, _coke.Id, 22000, 10, Start.AddDays(7), "jhb")), default)).Error!.Code);

        _supplier.Suspend();
        Assert.Equal("supplier.not_verified", (await Poster().Handle(new PostGroupDeal(_supplier.Id, new(_supplierPerson, _coke.Id, 22000, 10, Start.AddDays(7), "jhb")), default)).Error!.Code);
    }

    // ---- joining and privacy ----

    [Fact]
    public async Task A_shop_sees_the_group_total_and_progress_and_only_its_own_quantity()
    {
        var deal = await Posted(min: 10);
        var mine = NewShop();
        var other = NewShop();
        await Join(deal, other, 3);

        var joined = (await Join(deal, mine, 2)).Value!;

        Assert.Equal((5, 50, 2), (joined.TotalCases, joined.ProgressPercent, joined.YourCases));
        Assert.Null(joined.Shops);   // the number of shops is for the supplier, not for other shops
        var browse = (await Queries().Handle(new ListGroupDeals(other.Shop, other.Owner), default)).Value!.Single();
        Assert.Equal((5, 3), (browse.TotalCases, browse.YourCases));
    }

    [Fact]
    public async Task No_group_deal_response_carries_a_shops_name_or_id()
    {
        var deal = await Posted();
        var shop = NewShop();
        await Join(deal, shop, 5);

        var forShop = (await Queries().Handle(new ListGroupDeals(shop.Shop, shop.Owner), default)).Value!;
        var forSupplier = (await Queries().Handle(new ListSupplierGroupDeals(_supplier.Id, _supplierPerson), default)).Value!;

        foreach (var json in new[] { System.Text.Json.JsonSerializer.Serialize(forShop), System.Text.Json.JsonSerializer.Serialize(forSupplier) })
        {
            Assert.DoesNotContain(shop.Shop.ToString(), json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Shop ", json, StringComparison.Ordinal);
        }
        Assert.Equal(1, forSupplier.Single().Shops);
    }

    [Fact]
    public async Task Only_a_shop_in_the_groups_area_with_permission_can_join_and_browse()
    {
        var deal = await Posted(area: "jhb");
        var elsewhere = NewShop("cpt");
        var local = NewShop("jhb");

        Assert.Equal("group.not_in_area", (await Join(deal, elsewhere, 2)).Error!.Code);
        Assert.Equal("permission.denied", (await Joiner().Handle(new JoinGroupDeal(deal.Id, local.Shop, new(Guid.NewGuid(), 2)), default)).Error!.Code);
        Assert.Equal("group.not_found", (await Joiner().Handle(new JoinGroupDeal(Guid.NewGuid(), local.Shop, new(local.Owner, 2)), default)).Error!.Code);
        Assert.Empty((await Queries().Handle(new ListGroupDeals(elsewhere.Shop, elsewhere.Owner), default)).Value!);
        Assert.Equal("permission.denied", (await Queries().Handle(new ListGroupDeals(local.Shop, Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Queries().Handle(new ListSupplierGroupDeals(_supplier.Id, Guid.NewGuid()), default)).Error!.Code);
    }

    // ---- closing ----

    [Fact]
    public async Task A_group_that_reached_its_minimum_becomes_one_sent_order_per_shop_at_the_bulk_price()
    {
        var deal = await Posted(min: 10);
        var a = NewShop();
        var b = NewShop();
        await Join(deal, a, 6);
        await Join(deal, b, 4);
        _clock.Advance(TimeSpan.FromDays(7));

        var closed = (await Job().Handle(new CloseDueGroupDeals(), default)).Value!;

        Assert.Equal(("reached", 2), (closed.Single().Outcome, closed.Single().Orders));
        Assert.Equal(2, _orders.Orders.Count);
        var order = _orders.Orders.Single(o => o.ShopId == a.Shop);
        Assert.Equal((OrderStatus.Sent, OrderChannel.App, 6, 22000L), (order.Status, order.Channel, order.Lines.Single().OrderedCases, order.Lines.Single().CasePriceCents));
        Assert.Equal(6 * 22000 + 5000, order.TotalCents);   // the supplier's delivery fee applies
        Assert.Equal(OrderPaymentStatus.Owing, order.PaymentStatus);
        Assert.Equal(2, _queue.Sent.Count(m => m.TemplateCode == "group.update"));
        Assert.Contains("6 cases at R220.00", _queue.Sent.First().Values["summary"]);
    }

    [Fact]
    public async Task Running_the_closing_job_again_makes_no_second_order_and_no_second_message()
    {
        var deal = await Posted(min: 4);
        await Join(deal, NewShop(), 5);
        _clock.Advance(TimeSpan.FromDays(7));

        await Job().Handle(new CloseDueGroupDeals(), default);
        var again = (await Job().Handle(new CloseDueGroupDeals(), default)).Value!;

        Assert.Empty(again);
        Assert.Single(_orders.Orders);
        Assert.Single(_queue.Sent);
    }

    [Fact]
    public async Task A_group_that_falls_short_tells_everyone_creates_no_orders_and_charges_nobody()
    {
        var deal = await Posted(min: 10);
        await Join(deal, NewShop(), 3);
        await Join(deal, NewShop(), 3);
        _clock.Advance(TimeSpan.FromDays(7));

        var closed = (await Job().Handle(new CloseDueGroupDeals(), default)).Value!;

        Assert.Equal(("failed", 0), (closed.Single().Outcome, closed.Single().Orders));
        Assert.Empty(_orders.Orders);
        Assert.Equal(2, _queue.Sent.Count);
        Assert.All(_queue.Sent, m => Assert.Contains("Nothing was charged", m.Values["summary"]));
    }

    [Fact]
    public async Task Nothing_closes_before_the_deadline()
    {
        await Posted();
        _clock.Advance(TimeSpan.FromDays(6));

        Assert.Empty((await Job().Handle(new CloseDueGroupDeals(), default)).Value!);
    }

    [Fact]
    public async Task The_supplier_can_close_early_once_the_minimum_is_met_and_the_job_then_makes_the_orders()
    {
        var deal = await Posted(min: 4);
        await Join(deal, NewShop(), 3);

        Assert.Equal("group.minimum_not_met", (await Manager().Handle(new CloseGroupDealEarly(deal.Id, _supplierPerson), default)).Error!.Code);
        await Join(deal, NewShop(), 2);
        Assert.True((await Manager().Handle(new CloseGroupDealEarly(deal.Id, _supplierPerson), default)).IsSuccess);
        Assert.Equal("permission.denied", (await Manager().Handle(new CloseGroupDealEarly(deal.Id, Guid.NewGuid()), default)).Error!.Code);

        await Job().Handle(new CloseDueGroupDeals(), default);
        Assert.Equal(2, _orders.Orders.Count);
        Assert.Equal("group.closed", (await Join(deal, NewShop(), 1)).Error!.Code);
    }

    [Fact]
    public async Task Cancelling_tells_the_shops_and_nothing_is_ordered()
    {
        var deal = await Posted();
        await Join(deal, NewShop(), 3);

        Assert.True((await Manager().Handle(new CancelGroupDeal(deal.Id, _supplierPerson), default)).IsSuccess);
        _clock.Advance(TimeSpan.FromDays(8));
        await Job().Handle(new CloseDueGroupDeals(), default);

        Assert.Empty(_orders.Orders);
        Assert.Contains("cancelled this group deal", Assert.Single(_queue.Sent).Values["summary"]);
        Assert.Equal("group.not_open", (await Manager().Handle(new CancelGroupDeal(deal.Id, _supplierPerson), default)).Error!.Code);
    }

    [Fact]
    public async Task A_supplier_that_only_takes_payment_in_the_app_gets_in_app_orders()
    {
        _supplier.SetTerms(0, 0, PaymentTerms.InApp);
        var deal = await Posted(min: 2);
        await Join(deal, NewShop(), 3);
        _clock.Advance(TimeSpan.FromDays(7));

        await Job().Handle(new CloseDueGroupDeals(), default);

        Assert.Equal(OrderPaymentMethod.InApp, _orders.Orders.Single().PaymentMethod);
    }

    [Fact]
    public async Task Closing_a_group_of_500_shops_makes_500_orders_quickly()
    {
        var deal = await Posted(min: 100);
        for (var i = 0; i < 500; i++) await Join(deal, NewShop(), 1 + i % 5);
        _clock.Advance(TimeSpan.FromDays(7));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var closed = (await Job().Handle(new CloseDueGroupDeals(), default)).Value!;
        watch.Stop();

        Assert.Equal(500, closed.Single().Orders);
        Assert.Equal(500, _orders.Orders.Select(o => o.ShopId).Distinct().Count());
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(1), $"took {watch.Elapsed}");
    }
}
