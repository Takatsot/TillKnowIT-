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

    /// <summary>A variant of this product: the same category, with its own label, barcode, price and cost. The parent is not changed.</summary>
    public static Result<Product> RecordVariant(Product parent, Guid id, Guid staffId, Guid deviceId, string? label, string? barcode, long costCents, long priceCents, DateTimeOffset now)
    {
        if (parent.ParentProductId is not null) return Result<Product>.Fail("variant.nested", "A variant cannot have variants of its own.");
        var cleanLabel = label?.Trim() ?? "";
        if (cleanLabel.Length is 0 or > 60) return Result<Product>.Fail("variant.label_invalid", "Describe the variant, for example 'M / Blue' (up to 60 characters).");

        var input = Check(new ProductInput(id, parent.ShopId, staffId, deviceId, null, $"{parent.Name} - {cleanLabel}", barcode, parent.CategoryCode, costCents, priceCents,
            parent.LowStockLevel, null, true, now), now);
        if (!input.IsSuccess) return Result<Product>.Fail(input.Error!.Code, input.Error.Message);

        var variant = Record(input.Value!);
        variant.ParentProductId = parent.Id;
        variant.VariantLabel = cleanLabel;
        variant.SoldByWeight = parent.SoldByWeight;
        return Result<Product>.Ok(variant);
    }

    /// <summary>Flags set by the shop owner in setup. A product that is an add-on or sold by weight stays so until the owner changes it.</summary>
    public void SetSellingFlags(bool soldByWeight, bool isAddOn, bool requiresSerial = false)
    {
        SoldByWeight = soldByWeight;
        IsAddOn = isAddOn;
        RequiresSerial = requiresSerial;
        Touch();
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

    /// <summary>
    /// B2-1: a variant is a product of its own (own barcode, price, cost and stock) that belongs to a parent product, so the sale flow and stock
    /// need nothing new to sell one. Null for ordinary products and for the parent itself.
    /// </summary>
    public Guid? ParentProductId { get; private set; }

    /// <summary>How a variant differs from its parent, such as "M / Blue".</summary>
    public string? VariantLabel { get; private set; }

    /// <summary>B2-2: an add-on (cheese, extra shot) that is only sold with another item, so the till keeps it off the main grid.</summary>
    public bool IsAddOn { get; private set; }

    /// <summary>B6-3: each unit sold has a serial number recorded (a phone, a power tool).</summary>
    public bool RequiresSerial { get; private set; }

    /// <summary>B4-1: sold by weight. Stock is in grams and the price is per kilogram.</summary>
    public bool SoldByWeight { get; private set; }

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
