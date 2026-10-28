using Billing.Contracts;
using Identity.Contracts;
using Insights.Application;
using Insights.Contracts;
using Insights.Domain;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Contracts;

namespace Insights.Application.Tests;

public class AnonymityTests
{
    private static readonly DateOnly Day = new(2026, 10, 25);
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);

    private static List<ShopDay> Shops(int count, string area = "jhb", string category = "drinks", int units = 10, long revenue = 10_000) =>
        Enumerable.Range(0, count).Select(_ => new ShopDay(Guid.NewGuid(), area, category, units, revenue)).ToList();

    [Fact]
    public void A_figure_needs_at_least_ten_shops_and_nine_publish_nothing_at_all()
    {
        Assert.Empty(Aggregator.Build(Shops(9), Day, Now));
        var ten = Assert.Single(Aggregator.Build(Shops(10), Day, Now));
        Assert.Equal((10, 100, 100_000L), (ten.ShopCount, ten.Units, ten.RevenueCents));
        Assert.True(Anonymity.CanPublish(10));
        Assert.False(Anonymity.CanPublish(9));
    }

    [Fact]
    public void The_same_shop_selling_many_lines_still_counts_as_one_shop()
    {
        var one = Guid.NewGuid();
        var rows = Enumerable.Range(0, 20).Select(_ => new ShopDay(one, "jhb", "drinks", 1, 100)).ToList();

        Assert.Empty(Aggregator.Build(rows, Day, Now));
    }

    [Fact]
    public void Areas_and_categories_are_counted_separately_so_a_thin_one_is_left_out_while_a_full_one_is_kept()
    {
        var rows = Shops(12, "jhb", "drinks").Concat(Shops(4, "jhb", "airtime")).Concat(Shops(11, "dbn", "drinks")).ToList();

        var built = Aggregator.Build(rows, Day, Now);

        Assert.Equal([("dbn", "drinks", 11), ("jhb", "drinks", 12)], built.Select(a => (a.Area, a.Category, a.ShopCount)));
    }

    [Fact]
    public void The_published_row_has_nothing_that_could_name_a_shop_a_person_a_device_or_a_customer()
    {
        var banned = new[] { "shop", "person", "device", "customer", "staff", "owner", "created_by", "createdby", "phone", "name", "sale" };

        var names = typeof(AreaAggregate).GetProperties().Select(p => p.Name.ToLowerInvariant()).Where(n => n != "shopcount" && n != "deviceid" && n != "createdby").ToList();

        Assert.DoesNotContain(names, n => banned.Any(b => n.Contains(b)));
        // The base entity carries who made a row and on which till. An aggregate never sets them.
        var row = Assert.Single(Aggregator.Build(Shops(10), Day, Now));
        Assert.Null(row.CreatedBy);
        Assert.Null(row.DeviceId);
    }

    [Fact]
    public void Revenue_is_shown_as_a_band()
    {
        Assert.Equal("under R1 000", Aggregator.RevenueBand(99_999));
        Assert.Equal("R1 000 to R5 000", Aggregator.RevenueBand(100_000));
        Assert.Equal("R25 000 to R100 000", Aggregator.RevenueBand(9_999_999));
        Assert.Equal("over R100 000", Aggregator.RevenueBand(10_000_000));
    }
}

public class InsightFlowTests
{
    private sealed class Store : IInsightsStore
    {
        public List<InsightOptIn> OptIns { get; } = [];
        public List<AreaAggregate> Aggregates { get; } = [];
        public void Add(InsightOptIn o) => OptIns.Add(o);
        public Task<InsightOptIn?> OptIn(Guid shopId, CancellationToken ct) => Task.FromResult(OptIns.FirstOrDefault(o => o.ShopId == shopId));
        public Task<List<Guid>> OptedInShops(CancellationToken ct) => Task.FromResult(OptIns.Where(o => o.OptedIn).Select(o => o.ShopId).ToList());
        public Task ReplaceDay(DateOnly day, IReadOnlyList<AreaAggregate> rows, CancellationToken ct) { Aggregates.RemoveAll(a => a.Day == day); Aggregates.AddRange(rows); return Task.CompletedTask; }
        public Task<List<AreaAggregate>> Rows(string area, string? category, DateOnly from, DateOnly to, CancellationToken ct) =>
            Task.FromResult(Aggregates.Where(a => a.Area == area && a.Day >= from && a.Day <= to && (category == null || a.Category == category)).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Access : IOrganisationAccess
    {
        public HashSet<(Guid, string)> Granted { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, perm)));
    }

    private sealed class Plans : IEntitlements
    {
        public bool Insights { get; set; } = true;
        public Task<bool> Has(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult(Insights && feature == PlanFeatures.InsightReports);
        public Task<long?> LimitFor(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult<long?>(null);
    }

    private sealed class Shops : IShopDirectory
    {
        public Dictionary<Guid, ShopInfo> All { get; } = [];
        public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) => Task.FromResult(All.GetValueOrDefault(shopId));
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(All.Keys.ToList());
    }

    private sealed class Sales : ICategorySalesSource
    {
        public List<Guid> Read { get; } = [];
        public Task<List<CategoryDay>> Day(Guid shopId, DateOnly day, CancellationToken ct) { Read.Add(shopId); return Task.FromResult(new List<CategoryDay> { new("drinks", 5, 5_000) }); }
    }

    private readonly Store _store = new();
    private readonly Access _access = new();
    private readonly Plans _plans = new();
    private readonly Shops _shops = new();
    private readonly Sales _sales = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 2, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _supplierOrg = Guid.NewGuid();
    private readonly Guid _supplierPerson = Guid.NewGuid();

    public InsightFlowTests() => _access.Granted.Add((_supplierPerson, "supplier.manage"));

    private InsightHandlers Handlers => new(_store, _access, _plans, _shops, _sales, _clock);

    private async Task<List<Guid>> OptedInShops(int count, string area = "JHB", string status = "verified")
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            _shops.All[id] = new ShopInfo(id, "S", status, _owner, null, null, "en", area);
            _access.Granted.Add((_owner, "shop.settings"));
            await Handlers.Handle(new SetInsightOptIn(id, new(_owner, true)), default);
        }
        return ids;
    }

    [Fact]
    public async Task Sharing_is_off_until_the_owner_turns_it_on_and_can_be_changed_any_time_with_a_clear_explanation()
    {
        _access.Granted.Add((_owner, "shop.settings"));
        var shop = Guid.NewGuid();

        var before = (await Handlers.Handle(new GetInsightOptIn(shop, _owner), default)).Value!;
        Assert.False(before.OptedIn);
        Assert.Contains("at least 10 shops", before.Explanation);
        Assert.Contains("off until you turn it on", before.Explanation);

        Assert.True((await Handlers.Handle(new SetInsightOptIn(shop, new(_owner, true)), default)).Value!.OptedIn);
        Assert.False((await Handlers.Handle(new SetInsightOptIn(shop, new(_owner, false)), default)).Value!.OptedIn);
        Assert.Single(_store.OptIns);
        Assert.Equal("permission.denied", (await Handlers.Handle(new SetInsightOptIn(shop, new(Guid.NewGuid(), true)), default)).Error!.Code);
    }

    [Fact]
    public async Task Only_shops_that_opted_in_are_read_and_a_thin_area_publishes_nothing()
    {
        var ten = await OptedInShops(10, "JHB");
        await OptedInShops(3, "DBN");
        var outShop = Guid.NewGuid();
        _shops.All[outShop] = new ShopInfo(outShop, "S", "verified", _owner, null, null, "en", "JHB");   // never opted in

        var published = (await Handlers.Handle(new RunInsightAggregation(null), default)).Value;

        Assert.Equal(1, published);
        Assert.DoesNotContain(outShop, _sales.Read);
        Assert.Equal(("jhb", 10), (_store.Aggregates.Single().Area, _store.Aggregates.Single().ShopCount));
        Assert.Equal(13, _sales.Read.Count);
        _ = ten;
    }

    [Fact]
    public async Task A_shop_that_opts_out_is_no_longer_counted_and_the_figure_disappears_below_ten()
    {
        var shops = await OptedInShops(10);
        await Handlers.Handle(new RunInsightAggregation(null), default);
        Assert.Single(_store.Aggregates);

        await Handlers.Handle(new SetInsightOptIn(shops[0], new(_owner, false)), default);
        await Handlers.Handle(new RunInsightAggregation(null), default);   // the same day again replaces it

        Assert.Empty(_store.Aggregates);
    }

    [Fact]
    public async Task Unverified_or_area_less_shops_and_unfinished_days_are_not_used()
    {
        await OptedInShops(10, "JHB", status: "pending");
        Assert.Equal(0, (await Handlers.Handle(new RunInsightAggregation(null), default)).Value);
        Assert.Equal("insight.day_not_finished", (await Handlers.Handle(new RunInsightAggregation(new DateOnly(2026, 10, 26)), default)).Error!.Code);
    }

    private async Task Seed(int shopsPerDay = 12)
    {
        await OptedInShops(shopsPerDay);
        foreach (var day in new[] { new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25) })
            await Handlers.Handle(new RunInsightAggregation(day), default);
    }

    private Task<TillKnowIT.SharedKernel.Result<InsightReportResponse>> Report(string area = "jhb", string audience = "supplier", Guid? person = null, string? category = null) =>
        Handlers.Handle(new GetInsightReport(new(person ?? _supplierPerson, audience == "supplier" ? _supplierOrg : _owner, audience, area, category, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))), default);

    [Fact]
    public async Task A_supplier_with_the_plan_gets_weekly_category_trends_with_bands_and_shop_counts_but_no_names()
    {
        await Seed();

        var report = (await Report()).Value!;

        Assert.Single(report.Rows);   // 19, 20 and 25 October are all in the week that starts on Monday 19 October
        var week = report.Rows.Single(r => r.WeekStart == new DateOnly(2026, 10, 19) && r.Units == 12 * 5 * 3);
        Assert.Equal(("drinks", "R1 000 to R5 000", 12), (week.Category, week.RevenueBand, week.Shops));
        Assert.All(report.Rows, r => Assert.True(r.Shops >= 10));
        Assert.Contains("at least 10 shops", report.Note);
    }

    [Fact]
    public async Task A_report_with_no_published_data_says_so_and_never_shows_a_thin_row()
    {
        await Seed(shopsPerDay: 9);

        var report = (await Report()).Value!;

        Assert.Empty(report.Rows);
        Assert.Contains("not enough data", report.Note);
        _store.Aggregates.Add(new AreaAggregate("jhb", "drinks", new DateOnly(2026, 10, 20), 5, 5_000, 3, _clock.GetUtcNow()));   // even a row that slipped in is not shown
        Assert.Empty((await Report()).Value!.Rows);
    }

    [Fact]
    public async Task Buying_a_report_needs_the_plan_the_right_person_a_known_audience_and_a_sensible_request()
    {
        await Seed();

        _plans.Insights = false;
        Assert.Equal("plan.feature_not_included", (await Report()).Error!.Code);
        _plans.Insights = true;
        Assert.Equal("permission.denied", (await Report(person: Guid.NewGuid())).Error!.Code);
        Assert.Equal("insight.audience_invalid", (await Report(audience: "bank")).Error!.Code);
        Assert.Equal("insight.area_required", (await Report(area: " ")).Error!.Code);
        Assert.Equal("insight.range_invalid", (await Handlers.Handle(new GetInsightReport(new(_supplierPerson, _supplierOrg, "supplier", "jhb", null, new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31))), default)).Error!.Code);
    }

    [Fact]
    public async Task A_category_filter_narrows_the_report()
    {
        await Seed();

        Assert.Empty((await Report(category: "airtime")).Value!.Rows);
        Assert.NotEmpty((await Report(category: "drinks")).Value!.Rows);
    }
}
