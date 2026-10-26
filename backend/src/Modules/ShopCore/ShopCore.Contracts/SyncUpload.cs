namespace ShopCore.Contracts;

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
/// One batch from one till, up to 500 records in total. Send products before the sales and movements that use them.
/// </summary>
public sealed record SyncUploadRequest(
    Guid ShopId, Guid DeviceId,
    List<SaleUpload>? Sales, List<StockMovementUpload>? StockMovements, List<SaleCancellationUpload>? Cancellations)
{
    public const int MaxRecords = 500;
    public int Count => (Sales?.Count ?? 0) + (StockMovements?.Count ?? 0) + (Cancellations?.Count ?? 0);
}

public static class UploadStatus
{
    public const string Accepted = "accepted";
    public const string Duplicate = "duplicate";
    public const string Rejected = "rejected";
}

/// <summary>Kind is sale, stock_movement or cancellation. Duplicates were already stored and are safe to drop from the queue.</summary>
public sealed record UploadRecordResult(string Kind, Guid Id, string Status, string? Code, string? Message);

public sealed record SyncUploadResponse(List<UploadRecordResult> Results);
