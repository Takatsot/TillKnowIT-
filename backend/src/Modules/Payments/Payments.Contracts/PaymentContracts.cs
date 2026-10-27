namespace Payments.Contracts;

/// <summary>ReturnUrl is where the partner's hosted checkout sends the owner back to; the card is never typed into our app.</summary>
public sealed record StartPaymentRequest(Guid CallerPersonId, string? ReturnUrl);

public sealed record PaymentAuditResponse(string? From, string To, string Reason, Guid? ActorPersonId, DateTimeOffset At);

/// <summary>Status is pending, held, released, refunded or failed. Frozen means a dispute is holding the money.</summary>
public sealed record PaymentResponse(
    Guid Id, Guid OrderId, long AmountCents, string Status, string? CheckoutUrl, DateTimeOffset? HeldAt, DateTimeOffset? ReleaseDueAt,
    bool Frozen, long ReleasedCents, long RefundedCents, DateTimeOffset? SettledAt, List<PaymentAuditResponse> Audit);

public sealed record RaiseDisputeRequest(Guid CallerPersonId, string? Reason);

/// <summary>Outcome is pay_supplier or refund_shop.</summary>
public sealed record ResolveDisputeRequest(Guid CallerPersonId, string? Outcome, string? Note);

public sealed record DisputeResponse(
    Guid Id, Guid OrderId, Guid ShopId, Guid SupplierId, string RaisedBy, string Reason, string Status, string? Outcome, string? Note,
    DateTimeOffset RaisedAt, DateTimeOffset? ResolvedAt);

public sealed record ReconciliationMismatch(string PartnerRef, string Kind, string Detail);

public sealed record ReconciliationResponse(DateOnly Day, int Matched, List<ReconciliationMismatch> Mismatches);
