using TillKnowIT.SharedKernel;

namespace Catalogue.Domain;

public enum CatalogueProductStatus { Suggested, Approved, Rejected }

/// <summary>Shared master list of products, identified by barcode.</summary>
public sealed class CatalogueProduct : VersionedEntity
{
    private CatalogueProduct() { }

    public CatalogueProduct(string barcode, string name, string? size, string categoryCode)
    {
        Barcode = barcode;
        Name = name;
        Size = size;
        CategoryCode = categoryCode;
    }

    public string Barcode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Size { get; private set; }
    public string CategoryCode { get; private set; } = default!;
    public string? PhotoUrl { get; private set; }
    public CatalogueProductStatus Status { get; private set; } = CatalogueProductStatus.Suggested;

    public void Approve()
    {
        Status = CatalogueProductStatus.Approved;
        Touch();
    }

    public void Reject()
    {
        Status = CatalogueProductStatus.Rejected;
        Touch();
    }
}
