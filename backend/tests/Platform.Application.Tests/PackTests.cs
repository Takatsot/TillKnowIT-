using Identity.Contracts;
using Platform.Application;
using Platform.Contracts;
using Platform.Domain;
using Platform.Infrastructure.Persistence;

namespace Platform.Application.Tests;

public class PackDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static PackInput Input(string code = "bakery", params string[] flags) =>
        new(code, "Bakery", "Bread and cakes", "slip.sale", flags.Select(f => new FeatureSetting(f, null)).ToList());

    [Fact]
    public void A_pack_has_a_code_a_name_a_template_and_only_known_features()
    {
        Assert.Equal("pack.code_invalid", BusinessPack.Create(Input("Bakery!"), Now).Error!.Code);
        Assert.Equal("pack.code_invalid", BusinessPack.Create(Input("1bakery"), Now).Error!.Code);
        Assert.Equal("pack.name_invalid", BusinessPack.Create(Input() with { Name = " " }, Now).Error!.Code);
        Assert.Equal("pack.template_invalid", BusinessPack.Create(Input() with { SlipTemplateCode = "" }, Now).Error!.Code);
        Assert.Equal("pack.feature_unknown", BusinessPack.Create(Input("bakery", "teleport"), Now).Error!.Code);
        Assert.Equal("pack.settings_invalid", BusinessPack.Create(Input() with { Features = [new(FeatureFlags.Weight, "[1,2]")] }, Now).Error!.Code);
        Assert.Equal("pack.settings_invalid", BusinessPack.Create(Input() with { Features = [new(FeatureFlags.Weight, "not json")] }, Now).Error!.Code);
        Assert.True(BusinessPack.Create(Input("bakery", FeatureFlags.Weight, FeatureFlags.Tips), Now).IsSuccess);
    }

    [Fact]
    public void Editing_replaces_the_features_and_raises_the_version_but_never_the_code()
    {
        var pack = BusinessPack.Create(Input("bakery", FeatureFlags.Weight), Now).Value!;
        var before = pack.Version;

        var edited = pack.Edit(Input("something_else", FeatureFlags.Tips, FeatureFlags.Tips, FeatureFlags.Refunds));

        Assert.True(edited.IsSuccess);
        Assert.Equal(("bakery", before + 1), (pack.Code, pack.Version));
        Assert.Equal([FeatureFlags.Refunds, FeatureFlags.Tips], pack.Features.Select(f => f.Flag).Order());   // listed once each
        Assert.False(pack.Has(FeatureFlags.Weight));
    }

    [Fact]
    public void A_bad_edit_changes_nothing()
    {
        var pack = BusinessPack.Create(Input("bakery", FeatureFlags.Weight), Now).Value!;

        Assert.Equal("pack.feature_unknown", pack.Edit(Input("bakery", "teleport")).Error!.Code);

        Assert.Equal(1, pack.Version);
        Assert.True(pack.Has(FeatureFlags.Weight));
    }
}

public class PackSeedTests
{
    [Fact]
    public void Every_seeded_feature_is_a_known_flag_and_every_pack_has_its_own_rows()
    {
        var packs = PackSeed.PackRows();
        var features = PackSeed.FeatureRows();

        Assert.Equal(["butchery", "cafe", "clothing_hardware", "salon", "spaza"], packs.Select(p => (string)p.GetType().GetProperty("Code")!.GetValue(p)!).Order());
        Assert.All(features, f => Assert.True(FeatureFlags.IsKnown((string)f.GetType().GetProperty("Flag")!.GetValue(f)!)));
        Assert.Equal(features.Length, features.Select(f => f.GetType().GetProperty("Id")!.GetValue(f)).Distinct().Count());
    }

    [Fact]
    public void The_spaza_pack_turns_nothing_extra_on_and_each_other_pack_turns_on_what_its_business_needs()
    {
        var byPack = PackSeed.FeatureRows().GroupBy(f => (Guid)f.GetType().GetProperty("PackId")!.GetValue(f)!)
            .ToDictionary(g => g.Key, g => g.Select(f => (string)f.GetType().GetProperty("Flag")!.GetValue(f)!).ToHashSet());
        HashSet<string> Of(string code) => byPack.GetValueOrDefault(TillKnowIT.SharedKernel.DeterministicGuid.From($"platform.pack:{code}")) ?? [];

        Assert.Empty(Of("spaza"));
        Assert.Contains(FeatureFlags.Tables, Of("cafe"));
        Assert.Contains(FeatureFlags.Kitchen, Of("cafe"));
        Assert.Contains(FeatureFlags.Weight, Of("butchery"));
        Assert.Contains(FeatureFlags.PriceEmbeddedBarcodes, Of("butchery"));
        Assert.Contains(FeatureFlags.Appointments, Of("salon"));
        Assert.Contains(FeatureFlags.Commission, Of("salon"));
        Assert.Contains(FeatureFlags.Variants, Of("clothing_hardware"));
        Assert.Contains(FeatureFlags.Layby, Of("clothing_hardware"));
        Assert.DoesNotContain(FeatureFlags.Tables, Of("salon"));
        Assert.DoesNotContain(FeatureFlags.Appointments, Of("cafe"));
    }
}

public class PackFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class Store : IPackStore
    {
        public List<BusinessPack> Packs { get; } = [];
        public List<ShopPack> ShopPacks { get; } = [];
        public List<ShopPackChange> Changes { get; } = [];
        public Task Add(BusinessPack p, CancellationToken ct) { Packs.Add(p); return Task.CompletedTask; }
        public Task<BusinessPack?> Get(string code, CancellationToken ct) => Task.FromResult(Packs.FirstOrDefault(p => p.Code == code));
        public Task<List<BusinessPack>> List(bool activeOnly, CancellationToken ct) => Task.FromResult(Packs.Where(p => !activeOnly || p.IsActive).ToList());
        public Task<ShopPack?> ShopPack(Guid shopId, CancellationToken ct) => Task.FromResult(ShopPacks.FirstOrDefault(s => s.ShopId == shopId));
        public Task Add(ShopPack s, CancellationToken ct) { ShopPacks.Add(s); return Task.CompletedTask; }
        public Task Add(ShopPackChange c, CancellationToken ct) { Changes.Add(c); return Task.CompletedTask; }
        public Task<int> ShopsOn(string code, CancellationToken ct) => Task.FromResult(ShopPacks.Count(s => s.PackCode == code));
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Access : IOrganisationAccess
    {
        public HashSet<(Guid, Guid)> Owners { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Owners.Contains((p, o)));
    }

    private sealed class Permissions : IPermissionChecker
    {
        public HashSet<Guid> Admins { get; } = [];
        public Task<bool> Has(Guid p, string perm, CancellationToken ct) => Task.FromResult(perm == "pack.manage" && Admins.Contains(p));
    }

    private readonly Store _store = new();
    private readonly Access _access = new();
    private readonly Permissions _permissions = new();
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();

    public PackFlowTests()
    {
        foreach (var (code, flags) in new[] { ("spaza", Array.Empty<string>()), ("cafe", [FeatureFlags.Tables, FeatureFlags.Kitchen]), ("salon", [FeatureFlags.Appointments]) })
            _store.Packs.Add(BusinessPack.Create(new(code, code, null, "slip.sale", flags.Select(f => new FeatureSetting(f, null)).ToList()), Now).Value!);
        _access.Owners.Add((_owner, _shop));
        _permissions.Admins.Add(_admin);
    }

    private ShopPackHandlers Shops => new(_store, new ShopFeatures(_store), _access, new FakeTimeProvider(Now));
    private PackAdminHandlers Admin => new(_store, _permissions, new FakeTimeProvider(Now));
    private ShopFeatures Features => new(_store);

    [Fact]
    public async Task A_shop_that_never_chose_is_on_the_default_pack_with_nothing_extra()
    {
        var pack = await Features.For(_shop, default);

        Assert.Equal(("spaza", 0), (pack.PackCode, pack.Features.Count));
        Assert.False(await Features.IsOn(_shop, FeatureFlags.Tables, default));
    }

    [Fact]
    public async Task Choosing_a_pack_turns_its_features_on_and_records_the_change_and_choosing_it_again_changes_nothing()
    {
        var chosen = await Shops.Handle(new ChooseShopPack(_shop, new(_owner, "cafe")), default);

        Assert.Equal("cafe", chosen.Value!.PackCode);
        Assert.True(await Features.IsOn(_shop, FeatureFlags.Tables, default));
        Assert.False(await Features.IsOn(_shop, FeatureFlags.Appointments, default));
        Assert.Equal([(null, "cafe")], _store.Changes.Select(c => (c.FromCode, c.ToCode)));

        await Shops.Handle(new ChooseShopPack(_shop, new(_owner, "cafe")), default);
        Assert.Single(_store.Changes);
    }

    [Fact]
    public async Task Switching_packs_swaps_the_features_and_switching_back_restores_them_exactly()
    {
        await Shops.Handle(new ChooseShopPack(_shop, new(_owner, "cafe")), default);
        await Shops.Handle(new ChooseShopPack(_shop, new(_owner, "salon")), default);

        Assert.True(await Features.IsOn(_shop, FeatureFlags.Appointments, default));
        Assert.False(await Features.IsOn(_shop, FeatureFlags.Tables, default));

        await Shops.Handle(new ChooseShopPack(_shop, new(_owner, "cafe")), default);

        Assert.True(await Features.IsOn(_shop, FeatureFlags.Tables, default));
        Assert.Equal([(null, "cafe"), ("cafe", "salon"), ("salon", "cafe")], _store.Changes.Select(c => (c.FromCode, c.ToCode)));
        Assert.Single(_store.ShopPacks);   // one row, one current pack
    }

    [Fact]
    public async Task Only_the_owner_chooses_and_only_an_available_pack()
    {
        Assert.Equal("permission.denied", (await Shops.Handle(new ChooseShopPack(_shop, new(Guid.NewGuid(), "cafe")), default)).Error!.Code);
        Assert.Equal("pack.not_found", (await Shops.Handle(new ChooseShopPack(_shop, new(_owner, "spaceship")), default)).Error!.Code);

        await Admin.Handle(new SetPackActive("cafe", _admin, false), default);
        Assert.Equal("pack.not_found", (await Shops.Handle(new ChooseShopPack(_shop, new(_owner, "cafe")), default)).Error!.Code);
        Assert.Empty(_store.ShopPacks);
    }

    [Fact]
    public async Task A_retired_pack_keeps_serving_the_shops_already_on_it()
    {
        await Shops.Handle(new ChooseShopPack(_shop, new(_owner, "cafe")), default);

        await Admin.Handle(new SetPackActive("cafe", _admin, false), default);

        Assert.True(await Features.IsOn(_shop, FeatureFlags.Tables, default));
        Assert.DoesNotContain("cafe", (await Shops.Handle(new ListBusinessPacks(), default)).Value!.Select(p => p.Code));
    }

    [Fact]
    public async Task The_platform_creates_and_edits_packs_and_a_new_version_is_made_each_time()
    {
        var created = await Admin.Handle(new SavePack(null, new(_admin, "bakery", "Bakery", null, "slip.sale", [new(FeatureFlags.Weight, null)])), default);
        var edited = await Admin.Handle(new SavePack("bakery", new(_admin, "bakery", "Bakery and cakes", null, "slip.sale", [new(FeatureFlags.Weight, null), new(FeatureFlags.Tips, null)])), default);

        Assert.Equal((1, 2, "Bakery and cakes"), (created.Value!.Version, edited.Value!.Version, edited.Value.Name));
        Assert.Equal("pack.code_taken", (await Admin.Handle(new SavePack(null, new(_admin, "bakery", "Again", null, "slip.sale", [])), default)).Error!.Code);
        Assert.Equal("pack.not_found", (await Admin.Handle(new SavePack("nothing", new(_admin, "nothing", "x", null, "slip.sale", [])), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Admin.Handle(new SavePack(null, new(Guid.NewGuid(), "x1", "x", null, "slip.sale", [])), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Admin.Handle(new ListAllBusinessPacks(_owner), default)).Error!.Code);
    }

    [Fact]
    public async Task The_default_pack_can_never_be_retired()
    {
        Assert.Equal("pack.default_required", (await Admin.Handle(new SetPackActive("spaza", _admin, false), default)).Error!.Code);
        Assert.True((await Admin.Handle(new SetPackActive("spaza", _admin, true), default)).IsSuccess);
    }

    [Fact]
    public async Task The_shops_pack_is_visible_to_the_owner_and_staff_but_not_strangers()
    {
        _access.Owners.Add((Guid.Parse("00000000-0000-0000-0000-000000000001"), _shop));

        Assert.True((await Shops.Handle(new GetShopPack(_shop, _owner), default)).IsSuccess);
        Assert.Equal("permission.denied", (await Shops.Handle(new GetShopPack(_shop, Guid.NewGuid()), default)).Error!.Code);
    }
}

internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
