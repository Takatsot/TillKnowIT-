using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

/// <summary>A shop's own copy of a product, with its own prices.</summary>
public sealed class Product : VersionedEntity
{
    private Product() { }

    public Product(Guid shopId, string name, string? barcode, string categoryCode, Money cost, Money price)
    {
        ShopId = shopId;
        Name = name;
        Barcode = barcode;
        CategoryCode = categoryCode;
        CostCents = cost.Cents;
        PriceCents = price.Cents;
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

    public void ChangePrice(Money cost, Money price)
    {
        CostCents = cost.Cents;
        PriceCents = price.Cents;
        Touch();
    }
}
