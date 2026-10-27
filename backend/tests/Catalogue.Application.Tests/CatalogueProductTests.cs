using Catalogue.Application.Products;
using Catalogue.Contracts;
using Catalogue.Domain;

namespace Catalogue.Application.Tests;

public class CatalogueProductTests
{
    private sealed class FakeStore : ICatalogueStore
    {
        public List<CatalogueProduct> Products { get; } = [];
        public int Saves { get; private set; }

        public Task<CatalogueProduct?> FindByBarcode(string barcode, CancellationToken ct) =>
            Task.FromResult(Products.FirstOrDefault(p => p.Barcode == barcode));
        public Task Add(CatalogueProduct product, CancellationToken ct) { Products.Add(product); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) { Saves++; return Task.CompletedTask; }
    }

    private sealed class FakeLookups : ILookupChecker
    {
        public Task<bool> IsActive(string type, string code, CancellationToken ct) => Task.FromResult(type == "categories" && code == "drinks");
    }

    private readonly FakeStore _store = new();

    private CatalogueProduct Approved(string barcode = "6001234567890")
    {
        var p = new CatalogueProduct(barcode, "Coke 330ml", "330ml", "drinks");
        p.Approve();
        _store.Products.Add(p);
        return p;
    }

    // ---- lookup (E2-1) ----

    [Fact]
    public async Task Finds_an_approved_product_by_barcode()
    {
        var p = Approved();
        var result = await new FindProductByBarcodeHandler(_store).Handle(new FindProductByBarcode(" 6001234567890 "), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(p.Id, result.Value!.Id);
        Assert.Equal("Coke 330ml", result.Value.Name);
        Assert.Equal("drinks", result.Value.CategoryCode);
    }

    [Fact]
    public async Task Unknown_barcode_is_not_found_so_the_till_can_open_the_short_form()
    {
        var result = await new FindProductByBarcodeHandler(_store).Handle(new FindProductByBarcode("6009999999999"), default);
        Assert.Equal("catalogue.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Unreviewed_and_rejected_products_are_not_shown_to_other_shops()
    {
        var suggested = new CatalogueProduct("6001111111111", "Unchecked", null, "drinks");
        var rejected = new CatalogueProduct("6002222222222", "Bad", null, "drinks");
        rejected.Reject();
        _store.Products.AddRange([suggested, rejected]);
        var handler = new FindProductByBarcodeHandler(_store);

        Assert.Equal("catalogue.not_found", (await handler.Handle(new FindProductByBarcode("6001111111111"), default)).Error!.Code);
        Assert.Equal("catalogue.not_found", (await handler.Handle(new FindProductByBarcode("6002222222222"), default)).Error!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abc123456")]
    [InlineData("123456789012345")]
    public async Task Malformed_barcode_is_rejected(string barcode)
    {
        var result = await new FindProductByBarcodeHandler(_store).Handle(new FindProductByBarcode(barcode), default);
        Assert.Equal("catalogue.barcode_invalid", result.Error!.Code);
    }

    // ---- suggestions ----

    [Fact]
    public async Task A_new_barcode_becomes_a_suggestion_waiting_for_review()
    {
        var result = await new SuggestProductHandler(_store, new FakeLookups())
            .Handle(new SuggestProduct("6003333333333", "  Fanta 330ml ", "330ml", "drinks"), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyKnown);
        Assert.Equal("suggested", result.Value.Status);
        var stored = Assert.Single(_store.Products);
        Assert.Equal("Fanta 330ml", stored.Name);
        Assert.Equal(CatalogueProductStatus.Suggested, stored.Status);
    }

    [Fact]
    public async Task Two_shops_suggesting_the_same_barcode_create_one_entry()
    {
        var handler = new SuggestProductHandler(_store, new FakeLookups());
        var first = await handler.Handle(new SuggestProduct("6003333333333", "Fanta", null, "drinks"), default);
        var second = await handler.Handle(new SuggestProduct("6003333333333", "Fanta orange", null, "drinks"), default);

        Assert.Single(_store.Products);
        Assert.True(second.Value!.AlreadyKnown);
        Assert.Equal(first.Value!.Id, second.Value.Id);
    }

    [Fact]
    public async Task Suggestion_for_an_approved_barcode_returns_the_approved_entry()
    {
        var p = Approved();
        var result = await new SuggestProductHandler(_store, new FakeLookups()).Handle(new SuggestProduct(p.Barcode, "Whatever", null, "drinks"), default);

        Assert.Equal("approved", result.Value!.Status);
        Assert.True(result.Value.AlreadyKnown);
        Assert.Single(_store.Products);
    }

    [Fact]
    public async Task Suggestion_needs_a_valid_barcode_name_and_category()
    {
        var handler = new SuggestProductHandler(_store, new FakeLookups());

        Assert.Equal("catalogue.barcode_invalid", (await handler.Handle(new SuggestProduct("12", "X", null, "drinks"), default)).Error!.Code);
        Assert.Equal("catalogue.name_invalid", (await handler.Handle(new SuggestProduct("6004444444444", " ", null, "drinks"), default)).Error!.Code);
        Assert.Equal("catalogue.category_invalid", (await handler.Handle(new SuggestProduct("6004444444444", "X", null, "guns"), default)).Error!.Code);
        Assert.Empty(_store.Products);
        Assert.Equal(0, _store.Saves);
    }
}
