using System.Text.Json;
using Identity.Contracts;
using Marketplace.Contracts;
using Payments.Contracts;
using Payments.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Payments.Application;

internal static class PaymentMapping
{
    public static PaymentResponse ToResponse(Payment p) => new(
        p.Id, p.OrderId, p.AmountCents, p.Status.ToString().ToLowerInvariant(), p.Status == PaymentStatus.Pending ? p.CheckoutUrl : null, p.HeldAt, p.ReleaseDueAt,
        p.Frozen, p.ReleasedCents, p.RefundedCents, p.SettledAt,
        p.Audit.OrderBy(a => a.CreatedAt).Select(a => new PaymentAuditResponse(a.FromStatus?.ToString().ToLowerInvariant(), a.ToStatus.ToString().ToLowerInvariant(), a.Reason, a.ActorPersonId, a.CreatedAt)).ToList());
}

/// <summary>
/// Pays the supplier out of held money and gives back the rest. The partner is asked first: if it fails, nothing changes here and the
/// payment stays held, so the next try (the release job, or the admin again) picks it up.
/// </summary>
public sealed class PaymentSettlement(IPaymentPartner partner, IOrderPayments orders, IPaymentStore payments)
{
    public async Task<Result> Settle(Payment payment, long releaseCents, string reason, Guid? actor, DateTimeOffset now, CancellationToken ct)
    {
        if (payment.Status != PaymentStatus.Held) return Result.Fail("payment.not_held", "There is no held money on this payment.");
        if (releaseCents < 0 || releaseCents > payment.AmountCents) return Result.Fail("payment.release_invalid", "That is more than was paid.");
        var refund = payment.AmountCents - releaseCents;

        if (releaseCents > 0)
        {
            var released = await partner.Release(payment.PartnerRef!, releaseCents, ct);
            if (!released.IsSuccess) return released;
        }
        if (refund > 0)
        {
            var refunded = await partner.Refund(payment.PartnerRef!, refund, ct);
            if (!refunded.IsSuccess) return refunded;
        }

        var settled = payment.Settle(releaseCents, reason, actor, now);
        if (!settled.IsSuccess) return settled;
        await payments.Save(ct);
        return await orders.SetPaymentStatus(payment.OrderId, releaseCents > 0 ? "paid" : "refunded", ct);
    }
}

// ---------------------------------------------------------------- M5-2: pay in the app

public sealed record StartPayment(Guid OrderId, StartPaymentRequest Request) : ICommand<PaymentResponse>;

internal sealed class StartPaymentHandler(IOrderPayments orders, IPaymentStore payments, IPaymentPartner partner, IOrganisationAccess access, TimeProvider clock)
    : ICommandHandler<StartPayment, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> Handle(StartPayment c, CancellationToken ct)
    {
        var order = await orders.Get(c.OrderId, ct);
        if (order is null) return Result<PaymentResponse>.Fail("order.not_found", "Order not found.");
        if (!await access.Has(c.Request.CallerPersonId, order.ShopId, "stock.receive", ct)) return Result<PaymentResponse>.Fail("permission.denied", "You do not have access to this order.");
        if (order.PaymentMethod != "in_app") return Result<PaymentResponse>.Fail("payment.not_in_app", "This order is paid on delivery.");
        if (order.Status != "confirmed") return Result<PaymentResponse>.Fail("payment.order_not_payable", "You can pay once the supplier has confirmed the order.");

        var existing = await payments.ForOrder(order.OrderId, ct);
        if (existing is { Status: PaymentStatus.Pending, CheckoutUrl: not null }) return Result<PaymentResponse>.Ok(PaymentMapping.ToResponse(existing));   // pick up where they left off
        if (existing is not null) return Result<PaymentResponse>.Fail("payment.already_paid", "This order has already been paid.");

        var started = Payment.Start(order.OrderId, order.ShopId, order.SupplierId, order.TotalCents, c.Request.CallerPersonId, clock.GetUtcNow());
        if (!started.IsSuccess) return Result<PaymentResponse>.Fail(started.Error!.Code, started.Error.Message);
        var payment = started.Value!;

        var checkout = await partner.CreateCheckout(new CheckoutRequest(payment.Id.ToString("N"), payment.AmountCents, $"Order {order.Reference} from {order.SupplierName}", c.Request.ReturnUrl), ct);
        if (!checkout.IsSuccess) return Result<PaymentResponse>.Fail(checkout.Error!.Code, checkout.Error.Message);

        payment.SetCheckout(checkout.Value!.PartnerRef, checkout.Value.Url);
        await payments.Add(payment, ct);
        await payments.Save(ct);
        return Result<PaymentResponse>.Ok(PaymentMapping.ToResponse(payment));
    }
}

// ---------------------------------------------------------------- the partner's callbacks

public sealed record HandlePaymentWebhook(string Partner, string? Signature, string Body) : ICommand<Unit>;

internal sealed class HandlePaymentWebhookHandler(IWebhookVerifier verifier, IPaymentStore payments, IOrderPayments orders, TimeProvider clock)
    : ICommandHandler<HandlePaymentWebhook, Unit>
{
    private sealed record Payload(string? Id, string? Type, string? PartnerRef, long? AmountCents);

    public async Task<Result<Unit>> Handle(HandlePaymentWebhook c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!verifier.Verify(c.Partner, c.Signature, c.Body, now)) return Result<Unit>.Fail("webhook.signature_invalid", "The callback could not be verified.");

        Payload? payload;
        try { payload = JsonSerializer.Deserialize<Payload>(c.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { payload = null; }
        if (payload is null || string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.Type) || string.IsNullOrWhiteSpace(payload.PartnerRef))
            return Result<Unit>.Fail("webhook.payload_invalid", "The callback is missing its id, type or reference.");

        // At-least-once delivery: a callback we have already handled does nothing the second time.
        if (await payments.HasReceipt(c.Partner, payload.Id, ct)) return Result<Unit>.Ok(default);

        var payment = await payments.ByPartnerRef(payload.PartnerRef, ct);
        if (payment is null) return Result<Unit>.Fail("payment.unknown", "No payment has that reference.");

        Result outcome = Result.Ok();
        var held = false;
        switch (payload.Type)
        {
            case "payment.held":
                outcome = payment.MarkHeld(payload.AmountCents ?? -1, now);
                held = outcome.IsSuccess && payment.Status == PaymentStatus.Held;
                break;
            case "payment.failed":
                payment.Fail("the partner reported the payment failed", null, now);
                break;
            // Anything else is not for us: acknowledged and ignored.
        }

        await payments.Add(new WebhookReceipt(c.Partner, payload.Id, payload.Type, now), ct);
        await payments.Save(ct);
        if (held)
        {
            var status = await orders.SetPaymentStatus(payment.OrderId, "held", ct);
            if (!status.IsSuccess) return Result<Unit>.Fail(status.Error!.Code, status.Error.Message);
        }
        return outcome.IsSuccess ? Result<Unit>.Ok(default) : Result<Unit>.Fail(outcome.Error!.Code, outcome.Error.Message);
    }
}

// ---------------------------------------------------------------- release on delivery

/// <summary>The shop confirmed the delivery (release now) or the supplier marked it delivered (start the 72 hours).</summary>
internal sealed class OrderDeliveredHandler(IPaymentStore payments, PaymentSettlement settlement, TimeProvider clock) : IDomainEventHandler<OrderDelivered>
{
    public async Task Handle(OrderDelivered e, CancellationToken ct)
    {
        var payment = await payments.ForOrder(e.OrderId, ct);
        if (payment is null || payment.Status != PaymentStatus.Held) return;

        var now = clock.GetUtcNow();
        var releaseNow = payment.Delivered(e.TotalCents, e.ByShop, now);
        await payments.Save(ct);
        if (!releaseNow) return;

        var result = await settlement.Settle(payment, payment.AmountToRelease, "shop confirmed the delivery", null, now, ct);
        // If the partner is down the money stays held and the release job tries again, because the due time is already set.
        if (!result.IsSuccess && result.Error!.Code != "payment.not_held") throw new InvalidOperationException($"Could not release payment {payment.Id}: {result.Error.Message}");
    }
}

public sealed record ReleaseDuePayments : ICommand<int>;

/// <summary>The worker's job: money held for 72 hours after the supplier marked delivery, with no dispute, goes to the supplier.</summary>
internal sealed class ReleaseDuePaymentsHandler(IPaymentStore payments, PaymentSettlement settlement, TimeProvider clock) : ICommandHandler<ReleaseDuePayments, int>
{
    public async Task<Result<int>> Handle(ReleaseDuePayments c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var released = 0;
        foreach (var payment in await payments.DueForRelease(now, 50, ct))
        {
            var result = await settlement.Settle(payment, payment.AmountToRelease, "72 hours passed with no dispute", null, now, ct);
            if (result.IsSuccess) released++;   // a failing partner call leaves it held for the next round
        }
        return Result<int>.Ok(released);
    }
}

// ---------------------------------------------------------------- reading

public sealed record GetOrderPayment(Guid OrderId, Guid CallerPersonId) : IQuery<PaymentResponse>;

internal sealed class GetOrderPaymentHandler(IOrderPayments orders, IPaymentStore payments, IOrganisationAccess access) : IQueryHandler<GetOrderPayment, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> Handle(GetOrderPayment q, CancellationToken ct)
    {
        var order = await orders.Get(q.OrderId, ct);
        if (order is null) return Result<PaymentResponse>.Fail("order.not_found", "Order not found.");
        var allowed = await access.Has(q.CallerPersonId, order.ShopId, "stock.receive", ct)
                      || (order.SupplierOrganisationId is { } org && await access.Has(q.CallerPersonId, org, "supplier.orders", ct));
        if (!allowed) return Result<PaymentResponse>.Fail("permission.denied", "You do not have access to this order.");

        var payment = await payments.ForOrder(q.OrderId, ct);
        return payment is null ? Result<PaymentResponse>.Fail("payment.not_found", "This order has no in-app payment.") : Result<PaymentResponse>.Ok(PaymentMapping.ToResponse(payment));
    }
}
