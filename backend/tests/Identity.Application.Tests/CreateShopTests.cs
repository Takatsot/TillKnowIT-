using Identity.Application.Shops;
using Identity.Domain;

namespace Identity.Application.Tests;

public class CreateShopTests
{
    private sealed class FakeStore(bool personExists = true) : IShopStore
    {
        public List<Organisation> Added { get; } = [];
        public bool Saved { get; private set; }
        public Task Add(Organisation shop, CancellationToken ct) { Added.Add(shop); return Task.CompletedTask; }
        public Task<bool> PersonExists(Guid personId, CancellationToken ct) => Task.FromResult(personExists);
        public Task Save(CancellationToken ct) { Saved = true; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Creates_shop_as_pending_until_a_field_agent_verifies_it()
    {
        var store = new FakeStore();
        var result = await new CreateShopHandler(store, new FakeLookups()).Handle(new CreateShop(Guid.NewGuid(), " Mama's Spaza ", "jhb", "zu"), default);

        Assert.True(result.IsSuccess);
        var shop = Assert.Single(store.Added);
        Assert.Equal("Mama's Spaza", shop.Name);
        Assert.Equal(OrganisationStatus.Pending, shop.Status);
        Assert.Equal(shop.Id, result.Value);
        Assert.True(store.Saved);
    }

    [Fact]
    public async Task Rejects_blank_name()
    {
        var store = new FakeStore();
        var result = await new CreateShopHandler(store, new FakeLookups()).Handle(new CreateShop(Guid.NewGuid(), "  ", "jhb", "en"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("shop.name_required", result.Error!.Code);
        Assert.Empty(store.Added);
    }

    [Fact]
    public async Task Rejects_unknown_owner()
    {
        var store = new FakeStore(personExists: false);
        var result = await new CreateShopHandler(store, new FakeLookups()).Handle(new CreateShop(Guid.NewGuid(), "Shop", "jhb", "en"), default);

        Assert.Equal("person.not_found", result.Error!.Code);
        Assert.False(store.Saved);
    }

    private sealed class FakeLookups : Catalogue.Contracts.ILookupChecker
    {
        public Task<bool> IsActive(string type, string code, CancellationToken ct) => Task.FromResult(type == "municipalities" && code is "jhb" or "cpt");
    }

    [Fact]
    public async Task The_shops_municipality_is_kept_so_suppliers_can_find_it()
    {
        var store = new FakeStore();
        var result = await new CreateShopHandler(store, new FakeLookups()).Handle(new CreateShop(Guid.NewGuid(), "Mama's Spaza", " jhb ", "zu"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("jhb", store.Added.Single().MunicipalityCode);
    }

    [Fact]
    public async Task An_unknown_municipality_is_refused_and_leaving_it_out_is_allowed()
    {
        var store = new FakeStore();
        var handler = new CreateShopHandler(store, new FakeLookups());

        Assert.Equal("shop.area_invalid", (await handler.Handle(new CreateShop(Guid.NewGuid(), "Spaza", "atlantis", "en"), default)).Error!.Code);
        Assert.Empty(store.Added);
        Assert.True((await handler.Handle(new CreateShop(Guid.NewGuid(), "Spaza", "", "en"), default)).IsSuccess);
        Assert.Null(store.Added.Single().MunicipalityCode);
    }
}
