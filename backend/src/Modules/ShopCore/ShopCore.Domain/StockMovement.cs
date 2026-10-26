using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public enum StockMovementType { Receive, Sale, Cancel, Count, WriteOff }

/// <summary>Append only. Stock on hand is the sum of quantities per product.</summary>
public sealed class StockMovement : Entity
{
    private StockMovement() { }

    public StockMovement(Guid shopId, Guid productId, StockMovementType type, int quantity,
        long? unitCostCents = null, string? reasonCode = null, string? supplierName = null, string? invoiceRef = null)
    {
        ShopId = shopId;
        ProductId = productId;
        Type = type;
        Quantity = quantity;
        UnitCostCents = unitCostCents;
        ReasonCode = reasonCode;
        SupplierName = supplierName;
        InvoiceRef = invoiceRef;
    }

    public Guid ShopId { get; private set; }
    public Guid ProductId { get; private set; }
    public StockMovementType Type { get; private set; }

    /// <summary>Signed: positive adds stock, negative removes it.</summary>
    public int Quantity { get; private set; }
    public long? UnitCostCents { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? SupplierName { get; private set; }
    public string? InvoiceRef { get; private set; }
}
