using Payments.Domain;
using TillKnowIT.SharedKernel;

namespace Payments.Application;

public interface IPaymentStore
{
    Task Add(Payment payment, CancellationToken ct);
    Task<Payment?> Get(Guid paymentId, CancellationToken ct);

    /// <summary>The order's current payment: the latest one that has not failed, or null.</summary>
    Task<Payment?> ForOrder(Guid orderId, CancellationToken ct);

    Task<Payment?> ByPartnerRef(string partnerRef, CancellationToken ct);
    Task<List<Payment>> DueForRelease(DateTimeOffset now, int take, CancellationToken ct);

    /// <summary>Payments that changed in the window, for the daily comparison with the partner.</summary>
    Task<List<Payment>> ChangedBetween(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<bool> HasReceipt(string partner, string eventId, CancellationToken ct);
    Task Add(WebhookReceipt receipt, CancellationToken ct);
    Task Add(ReconciliationRun run, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public interface IDisputeStore
{
    Task Add(Dispute dispute, CancellationToken ct);
    Task<Dispute?> Get(Guid disputeId, CancellationToken ct);
    Task<Dispute?> OpenForOrder(Guid orderId, CancellationToken ct);
    Task<List<Dispute>> Open(CancellationToken ct);
    Task Save(CancellationToken ct);
}

public sealed record CheckoutRequest(string Reference, long AmountCents, string Description, string? ReturnUrl);
public sealed record CheckoutSession(string PartnerRef, string Url);

/// <summary>One line of the partner's statement. State is pending, held, released, refunded or failed.</summary>
public sealed record PartnerTransaction(string PartnerRef, long AmountCents, string State, long ReleasedCents, long RefundedCents);

/// <summary>
/// The payment partner: hosted checkout, holding the money until delivery, paying the supplier and giving money back.
/// Card details never reach us. A real partner is connected later behind this port; until then only a development fake exists.
/// </summary>
public interface IPaymentPartner
{
    Task<Result<CheckoutSession>> CreateCheckout(CheckoutRequest request, CancellationToken ct);
    Task<Result> Release(string partnerRef, long amountCents, CancellationToken ct);
    Task<Result> Refund(string partnerRef, long amountCents, CancellationToken ct);
    Task<List<PartnerTransaction>> Statement(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public interface IWebhookVerifier
{
    bool Verify(string partner, string? signatureHeader, string body, DateTimeOffset now);
}
