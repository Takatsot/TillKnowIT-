namespace ShopCore.Contracts;

/// <summary>
/// A shop product created or edited on a till or in the back office. UpdatedAt is the time of the change on that device;
/// when two devices edit the same product, the later UpdatedAt wins.
/// </summary>
public sealed record ProductUpload(
    Guid Id, Guid StaffId, Guid? CatalogueProductId, string Name, string? Barcode, string CategoryCode,
    long CostCents, long PriceCents, int LowStockLevel, DateOnly? ExpiryDate, bool IsActive, DateTimeOffset UpdatedAt);

public sealed record SaleLineUpload(Guid ProductId, string Name, int Quantity, long PriceCents, long CostCents);

public sealed record SaleUpload(
    Guid Id, Guid StaffId, DateTimeOffset SoldAt, string PaymentMethodCode, long TotalCents, long VatCents,
    long? TenderedCents, long? ChangeCents, Guid? AccountId, List<SaleLineUpload> Lines);

/// <summary>Type is receive, sale, cancel, count or write_off. Quantity is signed.</summary>
public sealed record StockMovementUpload(
    Guid Id, Guid StaffId, Guid ProductId, string Type, int Quantity, DateTimeOffset CreatedAt, Guid? SaleId,
    long? UnitCostCents, string? ReasonCode, string? SupplierName, string? InvoiceRef);

public sealed record SaleCancellationUpload(Guid Id, Guid StaffId, Guid SaleId, string Reason, DateTimeOffset CancelledAt);

/// <summary>
/// One batch from one till, up to 500 records in total. Products in the batch are saved first, so a sale
/// can arrive together with a product created just before it.
/// </summary>
public sealed record SyncUploadRequest(
    Guid ShopId, Guid DeviceId,
    List<SaleUpload>? Sales, List<StockMovementUpload>? StockMovements, List<SaleCancellationUpload>? Cancellations,
    List<ProductUpload>? Products = null)
{
    public const int MaxRecords = 500;
    public int Count => (Sales?.Count ?? 0) + (StockMovements?.Count ?? 0) + (Cancellations?.Count ?? 0) + (Products?.Count ?? 0);
}

public static class UploadStatus
{
    public const string Accepted = "accepted";
    public const string Duplicate = "duplicate";
    public const string Rejected = "rejected";
}

/// <summary>Kind is product, sale, stock_movement or cancellation. Duplicates were already stored and are safe to drop from the queue.</summary>
public sealed record UploadRecordResult(string Kind, Guid Id, string Status, string? Code, string? Message);

public sealed record SyncUploadResponse(List<UploadRecordResult> Results);

/// <summary>Create or edit one product from the back office or a till that is online. The device must be an approved device of the shop.</summary>
public sealed record SaveProductRequest(Guid ShopId, Guid DeviceId, ProductUpload Product);
