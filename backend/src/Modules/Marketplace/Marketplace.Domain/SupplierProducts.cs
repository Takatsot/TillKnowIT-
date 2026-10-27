using System.Text.RegularExpressions;
using TillKnowIT.SharedKernel;

namespace Marketplace.Domain;

public sealed record SupplierProductInput(
    string? Barcode, string Name, int UnitsPerCase, long CasePriceCents, bool VatIncluded, bool InStock, string? ExpiryPolicy);

/// <summary>M2-1: what a supplier sells, by the case, with the case size and whether the price includes VAT.</summary>
public sealed partial class SupplierProduct : VersionedEntity
{
    private SupplierProduct() { }

    public static Result<SupplierProductInput> Check(SupplierProductInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Name) || i.Name.Trim().Length > 200)
            return Result<SupplierProductInput>.Fail("supplier_product.name_invalid", "A product needs a name (up to 200 characters).");
        if (i.Barcode is not null && !BarcodePattern().IsMatch(i.Barcode.Trim()))
            return Result<SupplierProductInput>.Fail("supplier_product.barcode_invalid", "A barcode is 6 to 14 digits.");
        if (i.UnitsPerCase is < 1 or > 10_000)
            return Result<SupplierProductInput>.Fail("supplier_product.case_size_invalid", "A case holds between 1 and 10,000 units.");
        if (i.CasePriceCents < 0)
            return Result<SupplierProductInput>.Fail("supplier_product.price_invalid", "A price cannot be negative.");
        if (i.ExpiryPolicy is { Length: > 200 })
            return Result<SupplierProductInput>.Fail("supplier_product.expiry_policy_invalid", "The expiry policy is too long.");
        return Result<SupplierProductInput>.Ok(i with
        {
            Name = i.Name.Trim(),
            Barcode = string.IsNullOrWhiteSpace(i.Barcode) ? null : i.Barcode.Trim(),
            ExpiryPolicy = string.IsNullOrWhiteSpace(i.ExpiryPolicy) ? null : i.ExpiryPolicy.Trim(),
        });
    }

    public static Result<SupplierProduct> Create(Guid supplierId, Guid? catalogueProductId, SupplierProductInput input, DateTimeOffset now)
    {
        var checkedInput = Check(input);
        if (!checkedInput.IsSuccess) return Result<SupplierProduct>.Fail(checkedInput.Error!.Code, checkedInput.Error.Message);
        var product = new SupplierProduct { SupplierId = supplierId, CatalogueProductId = catalogueProductId };
        product.StampFromDevice(Guid.CreateVersion7(), now, null, null);
        product.Apply(checkedInput.Value!);
        return Result<SupplierProduct>.Ok(product);
    }

    public Result Update(SupplierProductInput input, Guid? catalogueProductId)
    {
        var checkedInput = Check(input);
        if (!checkedInput.IsSuccess) return Result.Fail(checkedInput.Error!.Code, checkedInput.Error.Message);
        Apply(checkedInput.Value!);
        CatalogueProductId = catalogueProductId ?? CatalogueProductId;
        Touch();
        return Result.Ok();
    }

    private void Apply(SupplierProductInput i)
    {
        Barcode = i.Barcode;
        Name = i.Name;
        UnitsPerCase = i.UnitsPerCase;
        CasePriceCents = i.CasePriceCents;
        VatIncluded = i.VatIncluded;
        InStock = i.InStock;
        ExpiryPolicy = i.ExpiryPolicy;
    }

    public bool Matches(SupplierProductInput i) =>
        Barcode == i.Barcode && Name == i.Name && UnitsPerCase == i.UnitsPerCase && CasePriceCents == i.CasePriceCents
        && VatIncluded == i.VatIncluded && InStock == i.InStock && ExpiryPolicy == i.ExpiryPolicy;

    public Guid SupplierId { get; private set; }

    /// <summary>The shared catalogue entry, found by barcode. Null when the barcode is not in the catalogue (yet).</summary>
    public Guid? CatalogueProductId { get; private set; }
    public string? Barcode { get; private set; }
    public string Name { get; private set; } = default!;
    public int UnitsPerCase { get; private set; } = 1;
    public long CasePriceCents { get; private set; }
    public bool VatIncluded { get; private set; }
    public bool InStock { get; private set; } = true;
    public string? ExpiryPolicy { get; private set; }

    /// <summary>Price per unit to the nearest cent, halves up.</summary>
    public long UnitPriceCents => (CasePriceCents * 2 + UnitsPerCase) / (UnitsPerCase * 2);

    [GeneratedRegex(@"^\d{6,14}$")]
    private static partial Regex BarcodePattern();
}

public enum CatalogueAction { Create, Update, Unchanged, Error }

/// <summary>One row of a supplier's catalogue upload, as it will be saved. Issues with severity error stop the row; warnings do not.</summary>
public sealed record CatalogueLine(
    int Row, string? Barcode, string Name, int UnitsPerCase, long CasePriceCents, bool VatIncluded, bool InStock,
    Guid? CatalogueProductId, string Action, List<string> Issues);

/// <summary>One row as read from the sheet, before any checks.</summary>
public sealed record RawCatalogueRow(
    int Row, string? Barcode, string? Name, int? UnitsPerCase, long? CaseCostCents, long? UnitCostCents, bool? VatIncluded, bool? InStock);

public static class SupplierCatalogue
{
    /// <summary>Barcode to the shared catalogue's name and id, for approved catalogue products only.</summary>
    public sealed record CatalogueMatch(Guid Id, string Name);

    /// <summary>
    /// Works out what an upload would do: create new products, update changed ones, leave identical ones alone, and name
    /// every row that cannot be used. A barcode found in the shared catalogue links the product to it, and supplies the
    /// name when the sheet has none. The case price comes from the case price column, or the unit price times the case size.
    /// </summary>
    public static List<CatalogueLine> Plan(
        IEnumerable<RawCatalogueRow> rows, IReadOnlyDictionary<string, SupplierProduct> existingByBarcode, IReadOnlyDictionary<string, CatalogueMatch> catalogue)
    {
        var seen = new HashSet<string>();
        var lines = new List<CatalogueLine>();
        foreach (var r in rows)
        {
            var issues = new List<string>();
            var barcode = string.IsNullOrWhiteSpace(r.Barcode) ? null : r.Barcode.Trim();
            CatalogueMatch? match = barcode is not null && catalogue.TryGetValue(barcode, out var m) ? m : null;
            var name = !string.IsNullOrWhiteSpace(r.Name) ? r.Name.Trim() : match?.Name ?? "";
            var perCase = r.UnitsPerCase ?? 1;
            if (r.UnitsPerCase is null) issues.Add("case_size_assumed_1");

            long? casePrice = r.CaseCostCents ?? (r.UnitCostCents is { } unit ? unit * perCase : null);
            if (casePrice is null) issues.Add("price_missing");
            if (barcode is not null && !seen.Add(barcode)) issues.Add("duplicate_in_file");
            if (r.VatIncluded is null) issues.Add("vat_assumed_excluded");

            var input = new SupplierProductInput(barcode, name, perCase, casePrice ?? 0, r.VatIncluded ?? false, r.InStock ?? true, null);
            var check = SupplierProduct.Check(input);
            if (!check.IsSuccess) issues.Add(check.Error!.Code);

            var fatal = issues.Any(IsError);
            var action = CatalogueAction.Error;
            if (!fatal)
                action = barcode is not null && existingByBarcode.TryGetValue(barcode, out var existing)
                    ? existing.Matches(check.Value!) ? CatalogueAction.Unchanged : CatalogueAction.Update
                    : CatalogueAction.Create;

            lines.Add(new CatalogueLine(r.Row, barcode, name, perCase, casePrice ?? 0, input.VatIncluded, input.InStock, match?.Id,
                action.ToString().ToLowerInvariant(), issues));
        }
        return lines;
    }

    // Warnings: the row is still usable, but the supplier should look.
    private static bool IsError(string issue) => issue is not ("case_size_assumed_1" or "vat_assumed_excluded");
}
