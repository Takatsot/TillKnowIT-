using Marketplace.Domain;

namespace Marketplace.Application.Tests;

public class SupplierCatalogueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Supplier = Guid.NewGuid();

    private static SupplierProductInput Input(string? barcode = "6001234567890", string name = "Coke 330ml", int perCase = 24, long casePrice = 18000, bool vat = true, bool stock = true) =>
        new(barcode, name, perCase, casePrice, vat, stock, null);

    // ---- supplier product ----

    [Fact]
    public void The_unit_price_is_the_case_price_over_the_case_size_to_the_nearest_cent()
    {
        Assert.Equal(750, SupplierProduct.Create(Supplier, null, Input(), Now).Value!.UnitPriceCents);
        Assert.Equal(334, SupplierProduct.Create(Supplier, null, Input(perCase: 3, casePrice: 1001), Now).Value!.UnitPriceCents);   // 333.67
        Assert.Equal(501, SupplierProduct.Create(Supplier, null, Input(perCase: 2, casePrice: 1001), Now).Value!.UnitPriceCents);   // 500.5 rounds up
    }

    [Fact]
    public void A_product_needs_a_name_a_good_barcode_a_sensible_case_and_a_price()
    {
        string Code(SupplierProductInput i) => SupplierProduct.Check(i).Error!.Code;
        Assert.Equal("supplier_product.name_invalid", Code(Input(name: " ")));
        Assert.Equal("supplier_product.barcode_invalid", Code(Input(barcode: "12ab")));
        Assert.Equal("supplier_product.case_size_invalid", Code(Input(perCase: 0)));
        Assert.Equal("supplier_product.case_size_invalid", Code(Input(perCase: 10_001)));
        Assert.Equal("supplier_product.price_invalid", Code(Input(casePrice: -1)));
        Assert.True(SupplierProduct.Check(Input(barcode: null)).IsSuccess);
    }

    [Fact]
    public void Updating_keeps_the_catalogue_link_and_matching_ignores_nothing()
    {
        var catalogueId = Guid.NewGuid();
        var product = SupplierProduct.Create(Supplier, catalogueId, Input(), Now).Value!;

        Assert.True(product.Matches(Input()));
        Assert.False(product.Matches(Input(casePrice: 18100)));
        Assert.False(product.Matches(Input(vat: false)));

        product.Update(Input(casePrice: 19000), null);
        Assert.Equal(19000, product.CasePriceCents);
        Assert.Equal(catalogueId, product.CatalogueProductId);     // a null does not wipe the link
        Assert.Equal(2, product.Version);
    }

    // ---- planning an upload ----

    private static Dictionary<string, SupplierProduct> Existing(params SupplierProduct[] products) =>
        products.Where(p => p.Barcode is not null).ToDictionary(p => p.Barcode!);

    private static readonly Dictionary<string, SupplierCatalogue.CatalogueMatch> NoCatalogue = [];

    [Fact]
    public void New_rows_are_created_changed_ones_updated_and_identical_ones_left_alone()
    {
        var same = SupplierProduct.Create(Supplier, null, Input("6001111111111", "Bread", 1, 1800), Now).Value!;
        var changed = SupplierProduct.Create(Supplier, null, Input("6002222222222", "Milk", 12, 20000), Now).Value!;

        var plan = SupplierCatalogue.Plan([
            new(2, "6001111111111", "Bread", 1, 1800, null, true, true),
            new(3, "6002222222222", "Milk", 12, 21000, null, true, true),
            new(4, "6003333333333", "Rice", 10, 15000, null, true, true),
        ], Existing(same, changed), NoCatalogue);

        Assert.Equal(["unchanged", "update", "create"], plan.Select(l => l.Action));
    }

    [Fact]
    public void A_barcode_in_the_shared_catalogue_links_the_product_and_supplies_a_missing_name()
    {
        var id = Guid.NewGuid();
        var catalogue = new Dictionary<string, SupplierCatalogue.CatalogueMatch> { ["6004444444444"] = new(id, "Coca-Cola 330ml can") };

        var line = SupplierCatalogue.Plan([new(2, "6004444444444", null, 24, 18000, null, true, true)], Existing(), catalogue).Single();

        Assert.Equal(id, line.CatalogueProductId);
        Assert.Equal("Coca-Cola 330ml can", line.Name);
        Assert.Equal("create", line.Action);
    }

    [Fact]
    public void The_case_price_comes_from_the_case_column_or_the_unit_price_times_the_case_size()
    {
        var lines = SupplierCatalogue.Plan([
            new(2, "6001111111111", "A", 24, 18000, null, true, true),
            new(3, "6002222222222", "B", 24, null, 750, true, true),
            new(4, "6003333333333", "C", 24, 18000, 999, true, true),      // the case price wins when both are given
        ], Existing(), NoCatalogue);

        Assert.Equal([18000L, 18000L, 18000L], lines.Select(l => l.CasePriceCents));
    }

    [Fact]
    public void Rows_with_no_price_no_name_a_bad_case_or_a_repeated_barcode_are_errors_and_say_why()
    {
        var lines = SupplierCatalogue.Plan([
            new(2, "6001111111111", "No price", 24, null, null, true, true),
            new(3, "6002222222222", null, 24, 1000, null, true, true),
            new(4, "6003333333333", "Zero case", 0, 1000, null, true, true),
            new(5, "6004444444444", "First", 1, 1000, null, true, true),
            new(6, "6004444444444", "Repeat", 1, 1100, null, true, true),
        ], Existing(), NoCatalogue);

        Assert.All(lines.Where(l => l.Row != 5), l => Assert.Equal("error", l.Action));
        Assert.Contains("price_missing", lines[0].Issues);
        Assert.Contains("supplier_product.name_invalid", lines[1].Issues);
        Assert.Contains("supplier_product.case_size_invalid", lines[2].Issues);
        Assert.Equal("create", lines[3].Action);
        Assert.Contains("duplicate_in_file", lines[4].Issues);
    }

    [Fact]
    public void Missing_vat_or_case_size_are_warnings_not_errors_and_vat_is_assumed_excluded()
    {
        var line = SupplierCatalogue.Plan([new(2, "6001111111111", "Bread", null, 1800, null, null, null)], Existing(), NoCatalogue).Single();

        Assert.Equal("create", line.Action);
        Assert.Contains("case_size_assumed_1", line.Issues);
        Assert.Contains("vat_assumed_excluded", line.Issues);
        Assert.False(line.VatIncluded);
        Assert.True(line.InStock);
        Assert.Equal(1, line.UnitsPerCase);
    }

    [Fact]
    public void Rows_without_a_barcode_are_created_each_time_because_there_is_nothing_to_match_on()
    {
        var line = SupplierCatalogue.Plan([new(2, null, "Loose sweets", 1, 5000, null, true, true)], Existing(), NoCatalogue).Single();
        Assert.Equal("create", line.Action);
        Assert.Null(line.Barcode);
    }
}
