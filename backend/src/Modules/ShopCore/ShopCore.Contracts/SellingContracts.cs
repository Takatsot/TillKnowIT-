namespace ShopCore.Contracts;

// ---------------------------------------------------------------- setup (the owner, in the back office)

public sealed record VariantInput(string Label, string? Barcode, long CostCents, long PriceCents);

public sealed record CreateVariantsRequest(Guid CallerPersonId, List<VariantInput> Variants);

public sealed record VariantResponse(Guid Id, Guid ParentProductId, string Name, string Label, string? Barcode, long CostCents, long PriceCents);

/// <summary>Sold by weight means stock is in grams and the price is per kilogram. An add-on is only sold with another item.</summary>
public sealed record SellingFlagsRequest(Guid CallerPersonId, bool SoldByWeight, bool IsAddOn, bool RequiresSerial);

public sealed record ModifierGroupRequest(Guid CallerPersonId, string Name, bool Required, int Min, int Max, List<Guid> OptionProductIds);

public sealed record ModifierOptionResponse(Guid ProductId, string Name, long PriceCents);

public sealed record ModifierGroupResponse(Guid Id, string Name, bool Required, int Min, int Max, List<ModifierOptionResponse> Options);

public sealed record AssignModifierGroupsRequest(Guid CallerPersonId, List<Guid> GroupIds);

public sealed record ComboItemDto(Guid ProductId, int Quantity);

/// <summary>Type is percent_off, amount_off, fixed_price or combo. Days are mon to sun (all if left out). From and To are local hours like 16:00.</summary>
public sealed record PromotionRequest(
    Guid CallerPersonId, string Name, string Type, List<Guid>? ProductIds, int Percent, long AmountCents, List<ComboItemDto>? Items,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, List<string>? Days, string? From, string? To);

public sealed record PromotionResponse(
    Guid Id, string Name, string Type, List<Guid> ProductIds, int Percent, long AmountCents, List<ComboItemDto> Items,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, List<string> Days, string? From, string? To, bool IsActive);

public sealed record SetActiveRequest(Guid CallerPersonId, bool Active);

public sealed record FloorTableDto(string Area, string Name, int Seats);

public sealed record FloorPlanRequest(Guid CallerPersonId, List<FloorTableDto> Tables);

public sealed record FloorTableResponse(Guid Id, string Area, string Name, int Seats);

// ---------------------------------------------------------------- pricing preview (online)

public sealed record BasketLineDto(Guid ProductId, int Quantity, long UnitPriceCents);

public sealed record PriceBasketRequest(Guid DeviceId, Guid StaffId, List<BasketLineDto> Lines, DateTimeOffset? At);

public sealed record AppliedPromotionDto(Guid PromotionId, string Name, long DiscountCents, int Times);

public sealed record PricedBasketResponse(long SubtotalCents, long DiscountCents, long TotalCents, Dictionary<Guid, long> DiscountsByProduct, List<AppliedPromotionDto> Applied);

public sealed record ValidateModifiersRequest(Guid DeviceId, Guid StaffId, List<Guid> ChosenProductIds);

// ---------------------------------------------------------------- tills

public sealed record TillCall(Guid DeviceId, Guid StaffId);

public sealed record TicketLineDto(Guid ProductId, string Name, int Quantity, long PriceCents, int UnitDivisor = 1, string? Note = null, List<Guid>? AddOnProductIds = null);

/// <summary>Id is made on the till, so opening a ticket twice never makes two.</summary>
public sealed record OpenTicketRequest(Guid DeviceId, Guid StaffId, Guid Id, string Label, Guid? TableId, List<TicketLineDto> Lines);

public sealed record EditTicketRequest(Guid DeviceId, Guid StaffId, List<TicketLineDto> Lines);

public sealed record MoveTicketRequest(Guid DeviceId, Guid StaffId, Guid? TableId);

public sealed record MergeTicketRequest(Guid DeviceId, Guid StaffId, Guid OtherTicketId);

public sealed record CloseTicketRequest(Guid DeviceId, Guid StaffId, Guid SaleId);

/// <summary>LockedForYou is true when another till is holding the ticket, so this one should show it read-only.</summary>
public sealed record TicketResponse(
    Guid Id, string Label, Guid? TableId, string Status, List<TicketLineDto> Lines, long TotalCents, Guid? HeldByDeviceId, DateTimeOffset? HeldUntil, bool LockedForYou);

public sealed record KitchenItemDto(string Name, int Quantity, string? Note = null);

public sealed record CreateKitchenOrderRequest(Guid DeviceId, Guid StaffId, Guid Id, string OrderNo, Guid? TicketId, Guid? SaleId, string? TableLabel, List<KitchenItemDto> Items);

/// <summary>Action is start, ready, collect or cancel.</summary>
public sealed record AdvanceKitchenOrderRequest(Guid DeviceId, Guid StaffId, string Action);

public sealed record KitchenOrderResponse(Guid Id, string OrderNo, string? TableLabel, string Status, List<KitchenItemDto> Items, DateTimeOffset CreatedAt, DateTimeOffset? ReadyAt);

public sealed record CreateQuoteRequest(
    Guid DeviceId, Guid StaffId, Guid Id, string Number, string? CustomerName, string? CustomerPhone, Guid? CustomerId, List<TicketLineDto> Lines, DateOnly ExpiresOn);

public sealed record QuoteResponse(
    Guid Id, string Number, string? CustomerName, string? CustomerPhone, List<TicketLineDto> Lines, long TotalCents, DateOnly ExpiresOn, string Status, Guid? TicketId);

public sealed record CreateLaybyRequest(
    Guid DeviceId, Guid StaffId, Guid Id, string Reference, string CustomerName, string? CustomerPhone, Guid? CustomerId, List<TicketLineDto> Lines,
    long DepositCents, string DepositMethod, DateOnly DueOn);

public sealed record PayLaybyRequest(Guid DeviceId, Guid StaffId, long AmountCents, string MethodCode);

public sealed record LaybyPaymentDto(long AmountCents, string MethodCode, DateTimeOffset PaidAt);

public sealed record LaybyResponse(
    Guid Id, string Reference, string CustomerName, string? CustomerPhone, List<TicketLineDto> Lines, long TotalCents, long PaidCents, long BalanceCents, DateOnly DueOn,
    string Status, long CancellationFeeCents, long RefundDueCents, List<LaybyPaymentDto> Payments);

public sealed record CollectLaybyRequest(Guid DeviceId, Guid StaffId, Guid SaleId);

/// <summary>PaidWith is cash, card or qr: how the customer paid for the voucher.</summary>
public sealed record IssueVoucherRequest(Guid DeviceId, Guid StaffId, long AmountCents, string PaidWith, string Reference, Guid? SaleId);

/// <summary>The code is only in the answer to issuing a voucher; after that only its last four characters are known.</summary>
public sealed record VoucherIssued(Guid Id, string Code, long BalanceCents, DateOnly? ExpiresOn);

public sealed record VoucherResponse(Guid Id, string CodeLast4, long BalanceCents, DateOnly? ExpiresOn, bool IsVoid, bool IsExpired);

public sealed record ReleaseVoucherRequest(Guid DeviceId, Guid StaffId, string Code, string Reference);

public sealed record RedeemVoucherRequest(Guid DeviceId, Guid StaffId, string Code, long AmountCents, string Reference, Guid? SaleId);

public sealed record SerialItem(Guid ProductId, string Serial);

public sealed record RecordSerialsRequest(Guid DeviceId, Guid StaffId, List<SerialItem> Items);

public sealed record SerialResponse(Guid ProductId, string ProductName, string Serial, Guid SaleId, DateTimeOffset SoldAt);

/// <summary>An online refund. Lines are product ids and quantities from the original sale. MethodCode may be left out to go back the way the sale was paid.</summary>
public sealed record OnlineRefundRequest(
    Guid DeviceId, Guid StaffId, Guid Id, string Reason, string? MethodCode, Guid? ApprovedBy, List<RefundLineUpload> Lines);

/// <summary>When the refund was paid back as a voucher, Voucher carries its code, shown once.</summary>
public sealed record OnlineRefundResponse(Guid RefundId, long AmountCents, string MethodCode, VoucherIssued? Voucher);
