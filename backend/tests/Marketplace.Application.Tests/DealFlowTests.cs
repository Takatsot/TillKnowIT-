using Identity.Contracts;
using Marketplace.Application.Offers;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Marketplace.Application.Tests;

public class DealFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeSuppliers : ISupplierStore, ISupplierProductStore
    {
        public Dictionary<Guid, Supplier> Suppliers { get; } = [];
        public List<SupplierProduct> Products { get; } = [];
        public List<string> AreaCodes { get; } = ["jhb", "cpt"];
        public Task Add(Supplier s, CancellationToken ct) { Suppliers[s.Id] = s; return Task.CompletedTask; }
        public Task<Supplier?> Get(Guid id, CancellationToken ct) => Task.FromResult(Suppliers.GetValueOrDefault(id));
        public Task<List<Supplier>> Pending(CancellationToken ct) => Task.FromResult(new List<Supplier>());
        public Task<List<SupplierArea>> Areas(Guid id, CancellationToken ct) => Task.FromResult(AreaCodes.Select(c => new SupplierArea(id, c, DeliveryDays.Mon)).ToList());
        public Task ReplaceAreas(Guid id, List<SupplierArea> a, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<string, SupplierProduct>> ByBarcode(Guid id, CancellationToken ct) => Task.FromResult(new Dictionary<string, SupplierProduct>());
        Task<SupplierProduct?> ISupplierProductStore.Get(Guid id, CancellationToken ct) => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));
        public Task<List<SupplierProduct>> List(Guid id, CancellationToken ct) => Task.FromResult(Products.Where(p => p.SupplierId == id).ToList());
        public Task Add(SupplierProduct p, CancellationToken ct) { Products.Add(p); return Task.CompletedTask; }
    }

    private sealed class FakeDeals : IDealStore
    {
        public List<Deal> Deals { get; } = [];
        public Dictionary<Guid, string> Names { get; } = [];
        public Task Add(Deal d, CancellationToken ct) { Deals.Add(d); return Task.CompletedTask; }
        public Task<Deal?> Get(Guid id, CancellationToken ct) => Task.FromResult(Deals.FirstOrDefault(d => d.Id == id));
        public Task<List<Deal>> ForSupplier(Guid id, CancellationToken ct) => Task.FromResult(Deals.Where(d => d.SupplierId == id).ToList());
        public Task<List<(Deal, string)>> ActiveInArea(string area, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(Deals.Where(d => d.IsActiveAt(now) && d.AppliesToArea(area)).Select(d => (d, Names.GetValueOrDefault(d.SupplierId, "?"))).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeAccess : IOrganisationAccess
    {
        public HashSet<(Guid, Guid, string)> Granted { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, o, perm)));
    }

    private readonly FakeSuppliers _suppliers = new();
    private readonly FakeDeals _deals = new();
    private readonly FakeAccess _access = new();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Supplier _supplier;
    private readonly SupplierProduct _coke;

    public DealFlowTests()
    {
        var org = Guid.NewGuid();
        _supplier = Supplier.Apply(new SupplierApplication(_owner, org, "Fresh Foods", null, "Sipho", "0821234567", null, null, 2), Now).Value!;
        _supplier.Verify(Guid.NewGuid(), SupplierVerification.Items, Now);
        _suppliers.Suppliers[_supplier.Id] = _supplier;
        _coke = SupplierProduct.Create(_supplier.Id, null, new SupplierProductInput("6001234567890", "Coke", 24, 24000, true, true, null), Now).Value!;
        _suppliers.Products.Add(_coke);
        _deals.Names[_supplier.Id] = _supplier.Name;
        _access.Granted.Add((_owner, org, "deals.manage"));
    }

    private sealed class FakeSponsorship : Billing.Contracts.ISponsoredPlacements
    {
        public bool Allowed { get; set; }
        public Task<bool> CanSponsor(Guid org, CancellationToken ct) => Task.FromResult(Allowed);
        public Task<bool> Charge(Guid org, string area, string reference, CancellationToken ct) => Task.FromResult(Allowed);
    }

    private CreateDealHandler Create() => new(_suppliers, _suppliers, _deals, _access, new FakeTimeProvider(Now));
    private CreateDealHandler CreateWith(FakeSponsorship sponsorship) => new(_suppliers, _suppliers, _deals, _access, new FakeTimeProvider(Now), sponsorship);
    private DealQueries Queries() => new(_suppliers, _deals, _access, new FakeTimeProvider(Now));

    private CreateDealRequest Request(string type = "discount", DealTermsDto? terms = null, string[]? areas = null, bool sponsored = false, int startDays = 0, int endDays = 20, string title = "Spring special") =>
        new(_owner, title, type, terms ?? new DealTermsDto(_coke.Id, 10, null, null, null, null, null), Now.AddDays(startDays), Now.AddDays(endDays), [.. areas ?? ["jhb"]], sponsored);

    [Fact]
    public async Task A_sponsored_deal_needs_a_paid_campaign_but_an_ordinary_deal_does_not()
    {
        var sponsorship = new FakeSponsorship { Allowed = false };

        var refused = await CreateWith(sponsorship).Handle(new CreateDeal(_supplier.Id, Request(sponsored: true)), default);
        var ordinary = await CreateWith(sponsorship).Handle(new CreateDeal(_supplier.Id, Request(sponsored: false)), default);
        sponsorship.Allowed = true;
        var paid = await CreateWith(sponsorship).Handle(new CreateDeal(_supplier.Id, Request(sponsored: true, title: "Paid")), default);

        Assert.Equal("deal.sponsorship_not_paid", refused.Error!.Code);
        Assert.True(ordinary.IsSuccess);
        Assert.Equal("Sponsored", paid.Value!.Label);
    }

    [Fact]
    public async Task A_verified_supplier_creates_a_deal_for_one_of_its_products_in_areas_it_serves()
    {
        var result = await Create().Handle(new CreateDeal(_supplier.Id, Request(sponsored: true)), default);

        var deal = result.Value!;
        Assert.Equal(("Spring special", "discount", "Fresh Foods", "Sponsored", true), (deal.Title, deal.Type, deal.SupplierName, deal.Label, deal.IsActive));
        Assert.Equal(_coke.Id, deal.Terms.SupplierProductId);
        Assert.Single(_deals.Deals);
    }

    [Fact]
    public async Task A_deal_is_refused_for_strangers_unverified_suppliers_products_that_are_not_theirs_or_areas_they_do_not_serve()
    {
        Assert.Equal("permission.denied", (await Create().Handle(new CreateDeal(_supplier.Id, Request() with { CallerPersonId = Guid.NewGuid() }), default)).Error!.Code);
        Assert.Equal("supplier.not_found", (await Create().Handle(new CreateDeal(Guid.NewGuid(), Request()), default)).Error!.Code);
        Assert.Equal("deal.type_invalid", (await Create().Handle(new CreateDeal(_supplier.Id, Request("raffle")), default)).Error!.Code);
        Assert.Equal("deal.product_unknown", (await Create().Handle(new CreateDeal(_supplier.Id, Request(terms: new DealTermsDto(Guid.NewGuid(), 10, null, null, null, null, null))), default)).Error!.Code);
        Assert.Equal("deal.area_not_served", (await Create().Handle(new CreateDeal(_supplier.Id, Request(areas: ["eth"])), default)).Error!.Code);
        Assert.Equal("deal.discount_invalid", (await Create().Handle(new CreateDeal(_supplier.Id, Request(terms: new DealTermsDto(_coke.Id, 95, null, null, null, null, null))), default)).Error!.Code);
        Assert.Empty(_deals.Deals);

        _supplier.Suspend();
        Assert.Equal("supplier.not_verified", (await Create().Handle(new CreateDeal(_supplier.Id, Request()), default)).Error!.Code);
    }

    [Fact]
    public async Task Free_goods_and_combo_deals_are_created_with_their_own_terms()
    {
        var other = SupplierProduct.Create(_supplier.Id, null, new SupplierProductInput("6002222222222", "Fanta", 24, 22000, true, true, null), Now).Value!;
        _suppliers.Products.Add(other);

        var free = await Create().Handle(new CreateDeal(_supplier.Id, Request("free_goods", new DealTermsDto(_coke.Id, null, null, 10, 1, null, null))), default);
        var combo = await Create().Handle(new CreateDeal(_supplier.Id, Request("combo", new DealTermsDto(null, null, null, null, null, [new(_coke.Id, 1), new(other.Id, 1)], 40000))), default);

        Assert.Equal(("free_goods", "combo"), (free.Value!.Type, combo.Value!.Type));
        Assert.Equal(2, combo.Value.Terms.Items!.Count);
    }

    [Fact]
    public async Task A_cancelled_deal_stops_being_active_and_only_the_supplier_can_cancel_it()
    {
        var id = (await Create().Handle(new CreateDeal(_supplier.Id, Request()), default)).Value!.Id;
        var cancel = new CancelDealHandler(_suppliers, _deals, _access);

        Assert.Equal("permission.denied", (await cancel.Handle(new CancelDeal(id, new(Guid.NewGuid())), default)).Error!.Code);
        Assert.True(_deals.Deals.Single().IsActiveAt(Now.AddDays(1)));

        Assert.True((await cancel.Handle(new CancelDeal(id, new(_owner)), default)).IsSuccess);
        Assert.False(_deals.Deals.Single().IsActiveAt(Now.AddDays(1)));
        Assert.Equal("deal.not_found", (await cancel.Handle(new CancelDeal(Guid.NewGuid(), new(_owner)), default)).Error!.Code);
    }

    [Fact]
    public async Task A_supplier_sees_all_its_own_deals_and_shops_browse_only_the_ones_running_now_in_their_area_soonest_to_end_first()
    {
        await Create().Handle(new CreateDeal(_supplier.Id, Request(title: "Ends later", endDays: 30)), default);
        await Create().Handle(new CreateDeal(_supplier.Id, Request(title: "Ends soon", endDays: 5, sponsored: true)), default);
        await Create().Handle(new CreateDeal(_supplier.Id, Request(title: "Starts next week", startDays: 7, endDays: 20)), default);
        await Create().Handle(new CreateDeal(_supplier.Id, Request(title: "Cape Town only", areas: ["cpt"])), default);

        var mine = (await Queries().Handle(new ListSupplierDeals(_supplier.Id, _owner), default)).Value!;
        var browse = (await Queries().Handle(new BrowseDeals("jhb"), default)).Value!;

        Assert.Equal(4, mine.Count);
        Assert.Equal(["Ends soon", "Ends later"], browse.Select(d => d.Title));
        Assert.Equal(["Sponsored", null], browse.Select(d => d.Label));
        Assert.Equal("permission.denied", (await Queries().Handle(new ListSupplierDeals(_supplier.Id, Guid.NewGuid()), default)).Error!.Code);
    }
}
