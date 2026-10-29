namespace ShopCore.Contracts;

/// <summary>A product as an approved till keeps it. UpdatedAt is for "changed since": the till asks only for what moved after the last time it asked.</summary>
public sealed record PulledProduct(
    Guid Id, string Name, string? Barcode, string CategoryCode, long CostCents, long PriceCents, int LowStockLevel, DateOnly? ExpiryDate, bool IsActive,
    bool SoldByWeight, bool IsAddOn, bool RequiresSerial, Guid? ParentProductId, string? VariantLabel, DateTimeOffset UpdatedAt);

public sealed record ProductPullResponse(List<PulledProduct> Products, DateTimeOffset AsOf);
