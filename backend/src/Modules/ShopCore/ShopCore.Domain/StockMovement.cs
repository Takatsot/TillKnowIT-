using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public enum StockMovementType { Receive, Sale, Cancel, Count, WriteOff }

public sealed record StockMovementInput(
    Guid Id, Guid ShopId, Guid ProductId, StockMovementType Type, int Quantity, Guid StaffId, Guid DeviceId,
    DateTimeOffset CreatedAt, Guid? SaleId, long? UnitCostCents, string? ReasonCode, string? SupplierName, string? InvoiceRef);

/// <summary>Append only. Stock on hand is the sum of quantities per product.</summary>
public sealed class StockMovement : Entity
{
    private StockMovement() { }

    /// <summary>
    /// Sign rules: receive and cancel add stock, sale and write-off remove it, a count is the signed difference.
    /// Counts and write-offs must say why.
    /// </summary>
    public static Result<StockMovement> Record(StockMovementInput i)
    {
        if (i.Quantity == 0)
            return Result<StockMovement>.Fail("stock.quantity_zero", "A stock movement cannot be zero.");

        var signOk = i.Type switch
        {
            StockMovementType.Receive or StockMovementType.Cancel => i.Quantity > 0,
            StockMovementType.Sale or StockMovementType.WriteOff => i.Quantity < 0,
            _ => true,
        };
        if (!signOk)
            return Result<StockMovement>.Fail("stock.quantity_sign", $"A {i.Type} movement has the wrong sign.");

        if (i.Type is StockMovementType.Count or StockMovementType.WriteOff && string.IsNullOrWhiteSpace(i.ReasonCode))
            return Result<StockMovement>.Fail("stock.reason_required", "Counts and write-offs need a reason.");
        if (i.Type is StockMovementType.Sale or StockMovementType.Cancel && i.SaleId is null)
            return Result<StockMovement>.Fail("stock.sale_required", "Sale and cancel movements must point at their sale.");
        if (i.UnitCostCents < 0)
            return Result<StockMovement>.Fail("stock.cost_invalid", "Cost cannot be negative.");

        var movement = new StockMovement
        {
            ShopId = i.ShopId,
            ProductId = i.ProductId,
            Type = i.Type,
            Quantity = i.Quantity,
            SaleId = i.SaleId,
            UnitCostCents = i.UnitCostCents,
            ReasonCode = i.ReasonCode,
            SupplierName = i.SupplierName,
            InvoiceRef = i.InvoiceRef,
        };
        movement.StampFromDevice(i.Id, i.CreatedAt, i.StaffId, i.DeviceId);
        return Result<StockMovement>.Ok(movement);
    }

    public Guid ShopId { get; private set; }
    public Guid ProductId { get; private set; }
    public StockMovementType Type { get; private set; }

    /// <summary>Signed: positive adds stock, negative removes it.</summary>
    public int Quantity { get; private set; }

    /// <summary>Set for sale and cancel movements, so a cancellation can be checked against what was sold.</summary>
    public Guid? SaleId { get; private set; }
    public long? UnitCostCents { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? SupplierName { get; private set; }
    public string? InvoiceRef { get; private set; }
}

public static class StockCalculator
{
    /// <summary>Stock on hand per product. Negative stock is allowed; it only means a count is due.</summary>
    public static Dictionary<Guid, int> OnHand(IEnumerable<(Guid ProductId, int Quantity)> movements) =>
        movements.GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));

    public static bool NeedsCount(int onHand) => onHand < 0;
}
