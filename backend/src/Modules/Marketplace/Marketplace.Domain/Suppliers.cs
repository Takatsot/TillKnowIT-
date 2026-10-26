using TillKnowIT.SharedKernel;

namespace Marketplace.Domain;

public sealed class Supplier : VersionedEntity
{
    private Supplier() { }

    public Supplier(Guid organisationId, string name, string? whatsapp, string? email)
    {
        OrganisationId = organisationId;
        Name = name;
        Whatsapp = whatsapp;
        Email = email;
    }

    public Guid OrganisationId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Whatsapp { get; private set; }
    public string? Email { get; private set; }
    public bool IsActive { get; private set; } = true;
}

public sealed class SupplierProduct : VersionedEntity
{
    private SupplierProduct() { }

    public SupplierProduct(Guid supplierId, string? barcode, string name, int unitsPerCase, long casePriceCents)
    {
        SupplierId = supplierId;
        Barcode = barcode;
        Name = name;
        UnitsPerCase = unitsPerCase;
        CasePriceCents = casePriceCents;
    }

    public Guid SupplierId { get; private set; }
    public string? Barcode { get; private set; }
    public string Name { get; private set; } = default!;
    public int UnitsPerCase { get; private set; }
    public long CasePriceCents { get; private set; }
    public bool IsAvailable { get; private set; } = true;
}

public enum PurchaseOrderStatus { Draft, Sent, Confirmed, Received, Cancelled }

public sealed class PurchaseOrder : VersionedEntity
{
    private PurchaseOrder() { }

    public PurchaseOrder(Guid shopId, Guid supplierId, IEnumerable<PurchaseOrderLine> lines)
    {
        ShopId = shopId;
        SupplierId = supplierId;
        Lines = lines.ToList();
    }

    public Guid ShopId { get; private set; }
    public Guid SupplierId { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public List<PurchaseOrderLine> Lines { get; private set; } = [];

    public void Send()
    {
        Status = PurchaseOrderStatus.Sent;
        Touch();
    }
}

public sealed class PurchaseOrderLine
{
    private PurchaseOrderLine() { }

    public PurchaseOrderLine(Guid supplierProductId, int quantityCases, long casePriceCents)
    {
        SupplierProductId = supplierProductId;
        QuantityCases = quantityCases;
        CasePriceCents = casePriceCents;
    }

    public Guid PurchaseOrderId { get; private set; }
    public Guid SupplierProductId { get; private set; }
    public int QuantityCases { get; private set; }
    public long CasePriceCents { get; private set; }
}
