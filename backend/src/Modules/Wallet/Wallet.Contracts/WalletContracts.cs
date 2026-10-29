namespace Wallet.Contracts;

// ---------------------------------------------------------------- schools and holders

public sealed record RegisterSchoolRequest(Guid CallerPersonId, string Name, string? EmisNumber, Guid ShopId);

public sealed record ConfigureSchoolRequest(
    Guid CallerPersonId, int FeeBasisPoints, string PayoutSchedule, bool StaffAccountsEnabled, long? DailyCeilingCents, int OfflineSplitMinutes);

public sealed record SchoolResponse(
    Guid Id, string Name, string? EmisNumber, Guid ShopId, string Status, string PayoutSchedule, int FeeBasisPoints, bool StaffAccountsEnabled,
    long? DailyCeilingCents, int OfflineSplitMinutes);

public sealed record AddSchoolAdminRequest(Guid CallerPersonId, Guid PersonId);

/// <summary>Type is learner or teacher.</summary>
public sealed record AddHolderRequest(Guid CallerPersonId, string FirstName, string LastName, string? Grade, string? ClassName, string Type);

public sealed record UpdateHolderRequest(Guid CallerPersonId, string? FirstName, string? LastName, string? Grade, string? ClassName);

public sealed record HolderResponse(Guid Id, Guid SchoolId, string Type, string FirstName, string LastName, string? Grade, string? ClassName, string Status, Guid WalletId, bool HasPhoto);

/// <summary>The link code is only here once, in the answer to creating the holder or asking for a new code.</summary>
public sealed record HolderWithCode(HolderResponse Holder, string LinkCode);

public sealed record ImportRowResult(int Row, string Name, string Outcome, string? Detail);

/// <summary>Outcome per row is created, duplicate or invalid. Nothing is created twice if the same file is sent again.</summary>
public sealed record ImportHoldersResult(int Created, int Duplicates, int Invalid, List<ImportRowResult> Rows, List<LinkCodeRow> LinkCodes);

public sealed record LinkCodeRow(Guid HolderId, string Name, string? Grade, string? ClassName, string LinkCode);

/// <summary>Label is what the parent sees to tell cards apart, such as "Blue wristband". Optional.</summary>
public sealed record IssueTokenRequest(Guid CallerPersonId, string Type, string RawToken, string? Label = null);

public sealed record TokenResponse(Guid Id, Guid HolderId, string Type, string Status, string? Label = null);

// ---------------------------------------------------------------- parents and wallets

/// <summary>Consent must be true, and the version is the text the parent was shown.</summary>
public sealed record LinkChildRequest(Guid ParentPersonId, string LinkCode, bool Consent, string ConsentVersion);

public sealed record ChildResponse(Guid HolderId, Guid WalletId, string Name, string? Grade, string? ClassName, string School, long BalanceCents, long? DailyLimitCents, string WalletStatus);

public sealed record WalletResponse(
    Guid Id, Guid HolderId, string HolderName, long BalanceCents, long? DailyLimitCents, long SpentTodayCents, string Status,
    List<RestrictionDto> Restrictions, bool NotifyOnPurchase, long? LowBalanceAlertCents);

/// <summary>Kind is product or category.</summary>
public sealed record RestrictionDto(string Kind, string Key);

public sealed record SetLimitsRequest(Guid CallerPersonId, long? DailyLimitCents, bool? NotifyOnPurchase, long? LowBalanceAlertCents, bool ClearLowBalanceAlert = false);

public sealed record SetRestrictionsRequest(Guid CallerPersonId, List<RestrictionDto> Restrictions);

public sealed record StatementLine(DateTimeOffset At, string Kind, string Description, long AmountCents, long BalanceAfterCents);

// ---------------------------------------------------------------- top-ups and transfers

public sealed record StartTopUpRequest(Guid CallerPersonId, long AmountCents, string? ReturnUrl);

public sealed record TopUpResponse(Guid Id, Guid WalletId, long AmountCents, string Status, string? CheckoutUrl);

public sealed record CashTopUpRequest(Guid CallerPersonId, Guid WalletId, long AmountCents, string Receipt);

public sealed record TransferRequest(Guid CallerPersonId, Guid FromWalletId, Guid ToWalletId, long AmountCents, string Reference);

// ---------------------------------------------------------------- paying at the till

/// <summary>
/// SaleId makes the request safe to repeat. Give either Token (an NFC card or wristband UID) or QrToken (from the learner's phone).
/// UseStaffAccount is for teachers on a monthly staff account.
/// </summary>
public sealed record WalletPaymentRequest(
    Guid DeviceId, Guid StaffId, Guid SaleId, string? Token, string? QrToken, long AmountCents, List<SaleItemDto> Items, bool UseStaffAccount = false);

public sealed record SaleItemDto(string? ProductId, string? CategoryCode, string Name, int Quantity, long PriceCents);

/// <summary>What the cashier sees: the name and photo to check against the person standing there.</summary>
public sealed record WalletPaymentResponse(
    Guid PurchaseId, Guid HolderId, string HolderName, string? Grade, string? ClassName, bool HasPhoto, long AmountCents, long? BalanceCents, bool Repeated);

public sealed record QrResponse(string Token, DateTimeOffset ExpiresAt);

/// <summary>Everything a till needs about one holder to take wallet payments, without the real balance.</summary>
public sealed record TillHolder(Guid HolderId, Guid WalletId, string Type, string Name, string? Grade, string? ClassName, string? PhotoUrl, string Status, long AllowanceCents, string AllowanceToken);

public sealed record TillTokenBlock(string TokenHash, string Status);

public sealed record TillRestriction(Guid WalletId, string Kind, string Key);

public sealed record TillSnapshot(DateOnly Day, List<TillHolder> Holders, List<TillTokenBlock> Tokens, List<TillRestriction> Restrictions, List<TillPreOrder> PreOrders);

public sealed record TillPreOrder(Guid Id, Guid HolderId, string HolderName, string? ClassName, string BreakCode, DateOnly CollectOn, string Status, List<PreOrderLineDto> Lines);

// ---------------------------------------------------------------- offline sales

public sealed record OfflineWalletSale(Guid SaleId, Guid WalletId, string AllowanceToken, long AmountCents, List<SaleItemDto> Items, DateTimeOffset SoldAt);

public sealed record SettleOfflineSalesRequest(Guid DeviceId, Guid StaffId, List<OfflineWalletSale> Sales);

/// <summary>Outcome is settled or rejected. Overdrawn means the wallet ran short but the shop is paid, because the sale was inside the signed allowance.</summary>
public sealed record OfflineSaleOutcome(Guid SaleId, string Outcome, string? Reason, bool Overdrawn);

public sealed record SettleOfflineSalesResult(List<OfflineSaleOutcome> Outcomes);

// ---------------------------------------------------------------- pre-orders

public sealed record BreakDto(string Code, string Name, string CutOff);

public sealed record SaveBreaksRequest(Guid CallerPersonId, List<BreakDto> Breaks);

public sealed record MenuItemDto(Guid Id, string Name, long PriceCents, string? ProductId, string? CategoryCode, bool IsAvailable);

public sealed record SaveMenuItemRequest(Guid CallerPersonId, string Name, long PriceCents, string? ProductId, string? CategoryCode, bool IsAvailable);

public sealed record MenuResponse(List<BreakDto> Breaks, List<MenuItemDto> Items);

public sealed record PreOrderLineDto(Guid MenuItemId, string Name, int Quantity, long PriceCents);

public sealed record PlacePreOrderRequest(Guid CallerPersonId, Guid HolderId, string BreakCode, DateOnly CollectOn, List<PlacePreOrderLine> Lines);

public sealed record PlacePreOrderLine(Guid MenuItemId, int Quantity);

public sealed record PreOrderResponse(Guid Id, Guid HolderId, string BreakCode, DateOnly CollectOn, string Status, long TotalCents, DateTimeOffset CutOffAt, List<PreOrderLineDto> Lines);

public sealed record TillPreOrderCall(Guid DeviceId, Guid StaffId);

// ---------------------------------------------------------------- money

public sealed record RefundRequestDto(Guid CallerPersonId);

public sealed record RefundResponse(Guid Id, Guid WalletId, long AmountCents, string Status);

public sealed record ReviewRefundRequest(Guid CallerPersonId, bool Approve);

public sealed record ReconciliationResponse(Guid Id, DateOnly Day, long PartnerTotalCents, long LedgerTotalCents, long DifferenceCents, string Status, string? Note);

public sealed record ResolveReconciliationRequest(Guid CallerPersonId, string Note);

public sealed record PayoutResponse(Guid Id, Guid ShopId, long AmountCents, string Status, DateOnly Day, string? Error);

public sealed record RunPayoutsResult(bool Blocked, string? BlockedReason, List<PayoutResponse> Payouts);

public sealed record WalletSalesSummary(long SalesCents, long FeesCents, long PaidOutCents, long OwedToShopCents, List<PayoutResponse> Payouts);

public sealed record ErasureRequestResponse(Guid Id, DateTimeOffset DueBy, bool Completed);

public sealed record StaffMonthLine(Guid HolderId, string Name, long OwedCents);

public sealed record StaffMonthStatement(string Month, long TotalOwedCents, List<StaffMonthLine> Teachers);
