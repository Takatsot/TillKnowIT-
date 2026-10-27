using System.Text.RegularExpressions;
using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public sealed record ProductInput(
    Guid Id, Guid ShopId, Guid StaffId, Guid DeviceId, Guid? CatalogueProductId, string Name, string? Barcode,
    string CategoryCode, long CostCents, long PriceCents, int LowStockLevel, DateOnly? ExpiryDate, bool IsActive,
    DateTimeOffset UpdatedAt);

/// <summary>A shop's own copy of a product, with its own prices.</summary>
public sealed partial class Product : VersionedEntity
{
    /// <summary>A wrong till clock must not win every future clash, so edits dated further ahead than this are refused.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromHours(24);

    private Product() { }

    public static Result<ProductInput> Check(ProductInput i, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(i.Name) || i.Name.Trim().Length > 200)
            return Result<ProductInput>.Fail("product.name_invalid", "A product needs a name (up to 200 characters).");
        if (i.Barcode is not null && !BarcodePattern().IsMatch(i.Barcode.Trim()))
            return Result<ProductInput>.Fail("product.barcode_invalid", "A barcode is 6 to 14 digits.");
        if (i.CostCents < 0 || i.PriceCents < 0)
            return Result<ProductInput>.Fail("product.price_invalid", "Cost and price cannot be negative.");
        if (i.LowStockLevel < 0)
            return Result<ProductInput>.Fail("product.low_stock_invalid", "The low-stock level cannot be negative.");
        if (i.UpdatedAt > now + MaxClockSkew)
            return Result<ProductInput>.Fail("product.date_invalid", "The change is dated in the future.");
        return Result<ProductInput>.Ok(i with { Name = i.Name.Trim(), Barcode = string.IsNullOrWhiteSpace(i.Barcode) ? null : i.Barcode.Trim() });
    }

    /// <summary>Creates a product from a till or the back office, keeping the id and time it gave. Run <see cref="Check"/> first.</summary>
    public static Product Record(ProductInput i)
    {
        var product = new Product { ShopId = i.ShopId };
        product.StampFromDevice(i.Id, i.UpdatedAt, i.StaffId, i.DeviceId);
        product.Apply(i);
        return product;
    }

    /// <summary>When this product last changed, by the clock of the device that changed it.</summary>
    public DateTimeOffset LastChangedAt => UpdatedAt ?? CreatedAt;

    /// <summary>
    /// Last change wins, by the time on the change. An older change is refused and the caller keeps the server's copy.
    /// </summary>
    public Result Update(ProductInput i)
    {
        if (i.UpdatedAt <= LastChangedAt)
            return Result.Fail("product.stale", "A newer change to this product is already saved.");
        Apply(i);
        StampUpdate(i.UpdatedAt);
        return Result.Ok();
    }

    private void Apply(ProductInput i)
    {
        CatalogueProductId = i.CatalogueProductId;
        Name = i.Name.Trim();
        Barcode = string.IsNullOrWhiteSpace(i.Barcode) ? null : i.Barcode.Trim();
        CategoryCode = i.CategoryCode;
        CostCents = i.CostCents;
        PriceCents = i.PriceCents;
        LowStockLevel = i.LowStockLevel;
        ExpiryDate = i.ExpiryDate;
        IsActive = i.IsActive;
    }

    public Guid ShopId { get; private set; }
    public Guid? CatalogueProductId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Barcode { get; private set; }
    public string CategoryCode { get; private set; } = default!;
    public long CostCents { get; private set; }
    public long PriceCents { get; private set; }
    public int LowStockLevel { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public bool IsActive { get; private set; } = true;

    [GeneratedRegex(@"^\d{6,14}$")]
    private static partial Regex BarcodePattern();
}
