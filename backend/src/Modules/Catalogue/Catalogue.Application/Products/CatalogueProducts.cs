using System.Text.RegularExpressions;
using Catalogue.Contracts;
using Catalogue.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Catalogue.Application.Products;

public interface ICatalogueStore
{
    Task<CatalogueProduct?> FindByBarcode(string barcode, CancellationToken ct);
    Task Add(CatalogueProduct product, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public static partial class BarcodeRules
{
    public static bool IsValid(string? barcode) => barcode is not null && Pattern().IsMatch(barcode);

    [GeneratedRegex(@"^\d{6,14}$")]
    private static partial Regex Pattern();
}

/// <summary>E2-1: the till scans a barcode and gets the name and photo. Only approved products are shown.</summary>
public sealed record FindProductByBarcode(string Barcode) : IQuery<CatalogueProductResponse>;

internal sealed class FindProductByBarcodeHandler(ICatalogueStore store) : IQueryHandler<FindProductByBarcode, CatalogueProductResponse>
{
    public async Task<Result<CatalogueProductResponse>> Handle(FindProductByBarcode query, CancellationToken ct)
    {
        var barcode = query.Barcode?.Trim();
        if (!BarcodeRules.IsValid(barcode))
            return Result<CatalogueProductResponse>.Fail("catalogue.barcode_invalid", "A barcode is 6 to 14 digits.");

        var product = await store.FindByBarcode(barcode!, ct);
        if (product is null || product.Status != CatalogueProductStatus.Approved)
            return Result<CatalogueProductResponse>.Fail("catalogue.not_found", "This barcode is not in the shared catalogue yet.");

        return Result<CatalogueProductResponse>.Ok(
            new CatalogueProductResponse(product.Id, product.Barcode, product.Name, product.Size, product.CategoryCode, product.PhotoUrl));
    }
}

/// <summary>A shop proposes a product that is not in the shared catalogue. A platform admin approves it later (E2-3).</summary>
public sealed record SuggestProduct(string Barcode, string Name, string? Size, string CategoryCode) : ICommand<SuggestProductResponse>;

internal sealed class SuggestProductHandler(ICatalogueStore store, ILookupChecker lookups) : ICommandHandler<SuggestProduct, SuggestProductResponse>
{
    public async Task<Result<SuggestProductResponse>> Handle(SuggestProduct command, CancellationToken ct)
    {
        var barcode = command.Barcode?.Trim();
        if (!BarcodeRules.IsValid(barcode))
            return Result<SuggestProductResponse>.Fail("catalogue.barcode_invalid", "A barcode is 6 to 14 digits.");
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 200)
            return Result<SuggestProductResponse>.Fail("catalogue.name_invalid", "A product needs a name (up to 200 characters).");
        if (!await lookups.IsActive("categories", command.CategoryCode, ct))
            return Result<SuggestProductResponse>.Fail("catalogue.category_invalid", "Unknown category.");

        // Two shops suggesting the same barcode must not create two entries.
        var existing = await store.FindByBarcode(barcode!, ct);
        if (existing is not null)
            return Result<SuggestProductResponse>.Ok(new SuggestProductResponse(existing.Id, existing.Status.ToString().ToLowerInvariant(), true));

        var product = new CatalogueProduct(barcode!, command.Name.Trim(), command.Size?.Trim(), command.CategoryCode);
        await store.Add(product, ct);
        await store.Save(ct);
        return Result<SuggestProductResponse>.Ok(new SuggestProductResponse(product.Id, "suggested", false));
    }
}
