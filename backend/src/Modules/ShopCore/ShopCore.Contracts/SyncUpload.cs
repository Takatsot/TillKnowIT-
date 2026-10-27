namespace ShopCore.Contracts;

/// <summary>
/// A shop product created or edited on a till or in the back office. UpdatedAt is the time of the change on that device;
/// when two devices edit the same product, the later UpdatedAt wins.
/// </summary>
public sealed record ProductUpload(
    Guid Id, Guid StaffId, Guid? CatalogueProductId, string Name, string? Barcode, string CategoryCode,
    long CostCents, long PriceCents, int LowStockLevel, DateOnly? ExpiryDate, bool IsActive, DateTimeOffset UpdatedAt);

/// <summary>A credit customer. Like products, the later UpdatedAt wins when two devices edit the same customer.</summary>
public sealed record CreditAccountUpload(
    Guid Id, Guid StaffId, string Name, string? Phone, long? CreditLimitCents, DateTimeOffset UpdatedAt);

/// <summary>
/// Type is sale, payment or cancel. AmountCents is signed: a credit sale is positive, a payment or a cancelled
/// credit sale is negative. PaymentMethodCode (cash, card or qr) is only for payments.
/// </summary>
public sealed record CreditEntryUpload(
    Guid Id, Guid StaffId, Guid AccountId, string Type, long AmountCents, Guid? SaleId, string? PaymentMethodCode,
    DateTimeOffset CreatedAt);

/// <summary>
/// End-of-day cash-up. ExpectedCents is what the till worked out; the server works it out again from its own
/// records and keeps its own figure, so over or short is always based on data that really synced.
/// </summary>
public sealed record CashUpUpload(
    Guid Id, Guid StaffId, DateOnly Day, long FloatCents, long ExpectedCents, long CountedCents, string? Note,
    DateTimeOffset CashedUpAt);

/// <summary>ExpiryOverrideBy is the manager who approved selling an expired item. Their role must allow sale.override_expiry.</summary>
public sealed record SaleLineUpload(Guid ProductId, string Name, int Quantity, long PriceCents, long CostCents, Guid? ExpiryOverrideBy = null);

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
    List<ProductUpload>? Products = null,
    List<CreditAccountUpload>? CreditAccounts = null,
    List<CreditEntryUpload>? CreditEntries = null,
    List<CashUpUpload>? CashUps = null)
{
    public const int MaxRecords = 500;
    public int Count => (Sales?.Count ?? 0) + (StockMovements?.Count ?? 0) + (Cancellations?.Count ?? 0) + (Products?.Count ?? 0)
        + (CreditAccounts?.Count ?? 0) + (CreditEntries?.Count ?? 0) + (CashUps?.Count ?? 0);
}

public static class UploadStatus
{
    public const string Accepted = "accepted";
    public const string Duplicate = "duplicate";
    public const string Rejected = "rejected";
}

/// <summary>Kind is product, credit_account, sale, stock_movement, cancellation, credit_entry or cash_up. Duplicates were already stored and are safe to drop from the queue.</summary>
public sealed record UploadRecordResult(string Kind, Guid Id, string Status, string? Code, string? Message);

public sealed record SyncUploadResponse(List<UploadRecordResult> Results);

/// <summary>Create or edit one product from the back office or a till that is online. The device must be an approved device of the shop.</summary>
public sealed record SaveProductRequest(Guid ShopId, Guid DeviceId, ProductUpload Product);

/// <summary>A credit customer with what they owe. OverLimit only warns; it never stops a sale.</summary>
public sealed record CreditAccountBalance(Guid Id, string Name, string? Phone, long? CreditLimitCents, long BalanceCents, bool OverLimit);

public sealed record CreditHistoryEntry(Guid Id, string Type, long AmountCents, Guid? SaleId, string? PaymentMethodCode, DateTimeOffset CreatedAt);

/// <summary>
/// One product in the stock list. Flags can be out, low, not_selling, expiring, expired and needs_count.
/// DaysLeft is null when nothing is selling or nothing is left.
/// </summary>
public sealed record StockRow(
    Guid ProductId, string Name, string? Barcode, int OnHand, int LowStockLevel, DateOnly? ExpiryDate,
    int? DaysLeft, int SuggestedOrderQuantity, List<string> Flags);

public sealed record PeriodSummaryResponse(int Days, long SalesCents, long ProfitCents, int Customers, long AverageSaleCents);

public sealed record BestSellerResponse(Guid ProductId, string Name, int Units, long RevenueCents, long ProfitCents);

/// <summary>E8-1 and E8-2: the owner's numbers. The three lists are the first 10 products each.</summary>
public sealed record InsightsResponse(
    PeriodSummaryResponse Today, PeriodSummaryResponse Week, PeriodSummaryResponse Month,
    List<BestSellerResponse> BestSellers, List<StockRow> RunningLow, List<StockRow> NotSelling, List<StockRow> Expiring);

/// <summary>Status is "valid", "expiring_soon" (30 days or less) or "expired". Documents with no expiry are always valid.</summary>
public sealed record ComplianceDocumentResponse(Guid Id, string TypeCode, string FileName, DateOnly? ExpiresOn, int? DaysLeft, string Status);

public sealed record DueComplianceReminder(Guid ShopId, Guid DocumentId, string TypeCode, DateOnly? ExpiresOn, int Reminder, string DedupeKey);

/// <summary>Channel is qr, whatsapp, sms or email. Destination is the phone number or email; QR needs none.</summary>
public sealed record RequestSlipRequest(Guid ShopId, Guid DeviceId, Guid StaffId, string Channel, string? Destination, string? Language);

/// <summary>Status is "shown" (QR) or "queued". Url is the slip link: draw it as the QR code, or it is what the message contains.</summary>
public sealed record SlipResponse(string Status, string Url);

public sealed record SlipLineView(string Name, int Quantity, long PriceCents);

/// <summary>What the slip link shows. No staff, customer or device details, by design.</summary>
public sealed record SlipView(string ShopName, string Date, List<SlipLineView> Lines, long TotalCents, long VatCents, string PaymentMethod, bool Cancelled);

/// <summary>
/// One line of the review table. Quantity is in units and the cost is per unit, so cases are already worked out.
/// MatchedBy is barcode, name or none; Candidates are suggested products when nothing matched. Issues can be
/// no_match, ambiguous_name, name_or_barcode_missing, quantity_invalid and cost_missing.
/// </summary>
public sealed record StockImportLineResponse(
    int Row, string Text, string? Barcode, Guid? ProductId, string MatchedBy, List<Guid> Candidates,
    int Quantity, int? UnitsPerCase, long? UnitCostCents, List<string> Issues);

/// <summary>Status is queued, reading, ready, confirmed or failed. Headers and Mapping are for sheets: the owner can correct the column choices.</summary>
public sealed record StockImportResponse(
    Guid Id, string Status, string Kind, string FileName, string? SupplierName, List<string>? Headers, Dictionary<string, string>? Mapping,
    List<StockImportLineResponse> Lines, string? Error);

public sealed record StartStockImportResponse(Guid ImportId, string Status);

public sealed record ConfirmedLineRequest(Guid ProductId, int Quantity, long? UnitCostCents);

public sealed record ConfirmStockImportRequest(Guid ShopId, Guid DeviceId, Guid StaffId, string? SupplierName, string? InvoiceRef, List<ConfirmedLineRequest> Lines);

public sealed record RemapStockImportRequest(Guid ShopId, Guid DeviceId, Guid StaffId, Dictionary<string, string> Mapping);

/// <summary>
/// Sell airtime, data or electricity. Id is the idempotency key: send the same Id again after a timeout and you get the same
/// answer, never a second purchase. ProductType is airtime, data or electricity; Recipient is a phone number or an 11-digit meter number.
/// </summary>
public sealed record VasSaleRequest(
    Guid Id, Guid ShopId, Guid DeviceId, Guid StaffId, string ProductType, string Recipient, long AmountCents, string? ProductCode);

/// <summary>
/// Status is pending, completed, failed or unknown. Unknown means the partner did not answer: do not sell again, check with
/// support first. Token is the electricity token to give the customer.
/// </summary>
public sealed record VasSaleResponse(Guid Id, string Status, long AmountCents, long CommissionCents, string? PartnerRef, string? Token, string? Message);
