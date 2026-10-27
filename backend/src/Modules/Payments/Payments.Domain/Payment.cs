using TillKnowIT.SharedKernel;

namespace Payments.Domain;

public enum PaymentStatus { Pending, Held, Released, Refunded, Failed }

/// <summary>
/// M5-2: one in-app payment for a purchase order. The partner's hosted checkout takes the card; we only ever hold the partner's
/// reference. The money is held until the shop confirms the delivery, or until 72 hours after the supplier marked it delivered
/// with no dispute. What is released can be less than what was paid when the delivery was short; the rest goes back to the shop.
/// </summary>
public sealed class Payment : VersionedEntity
{
    public static readonly TimeSpan ReleaseWindow = TimeSpan.FromHours(72);

    private readonly List<PaymentAudit> _audit = [];

    private Payment() { }

    public Guid OrderId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid SupplierId { get; private set; }
    public long AmountCents { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? PartnerRef { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public DateTimeOffset? HeldAt { get; private set; }

    /// <summary>What the shop owes for what arrived; set when the order is delivered.</summary>
    public long? DueCents { get; private set; }

    public DateTimeOffset? ReleaseDueAt { get; private set; }

    /// <summary>An open dispute freezes the payment: nothing is released until it is resolved.</summary>
    public bool Frozen { get; private set; }

    public long ReleasedCents { get; private set; }
    public long RefundedCents { get; private set; }
    public DateTimeOffset? SettledAt { get; private set; }
    public IReadOnlyCollection<PaymentAudit> Audit => _audit;

    public static Result<Payment> Start(Guid orderId, Guid shopId, Guid supplierId, long amountCents, Guid createdBy, DateTimeOffset now)
    {
        if (amountCents <= 0) return Result<Payment>.Fail("payment.amount_invalid", "There is nothing to pay on this order.");
        var p = new Payment { OrderId = orderId, ShopId = shopId, SupplierId = supplierId, AmountCents = amountCents, Status = PaymentStatus.Pending, CreatedBy = createdBy, CreatedAt = now };
        p.Record(null, PaymentStatus.Pending, "payment started", createdBy, now);
        return Result<Payment>.Ok(p);
    }

    public void SetCheckout(string partnerRef, string url)
    {
        PartnerRef = partnerRef;
        CheckoutUrl = url;
        Touch();
    }

    /// <summary>The partner says the card was charged and the money is held on the order's behalf.</summary>
    public Result MarkHeld(long paidCents, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Held) return Result.Ok();   // a repeated callback
        if (Status != PaymentStatus.Pending) return Result.Fail("payment.not_pending", "This payment is not waiting for money.");
        if (paidCents != AmountCents)
        {
            Fail($"the partner reported {paidCents} cents but {AmountCents} were expected", null, now);
            return Result.Fail("payment.amount_mismatch", "The amount paid does not match the order.");
        }
        Status = PaymentStatus.Held;
        HeldAt = now;
        Record(PaymentStatus.Pending, PaymentStatus.Held, "money held by the partner", null, now);
        Touch();
        return Result.Ok();
    }

    public void Fail(string reason, Guid? actor, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending) return;
        Status = PaymentStatus.Failed;
        Record(PaymentStatus.Pending, PaymentStatus.Failed, reason, actor, now);
        Touch();
    }

    /// <summary>
    /// The order was delivered. A shop's confirmation lets the money go now; a supplier's mark starts the 72 hours.
    /// Returns true when the payment should be released straight away.
    /// </summary>
    public bool Delivered(long dueCents, bool byShop, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Held) return false;
        DueCents = Math.Clamp(dueCents, 0, AmountCents);
        if (byShop)
        {
            ReleaseDueAt = now;
            Touch();
            return !Frozen;
        }
        ReleaseDueAt ??= now + ReleaseWindow;
        Touch();
        return false;
    }

    public bool IsDueForRelease(DateTimeOffset now) => Status == PaymentStatus.Held && !Frozen && ReleaseDueAt is { } due && due <= now;

    public void Freeze(Guid actor, DateTimeOffset now)
    {
        if (Frozen) return;
        Frozen = true;
        Record(Status, Status, "frozen by a dispute", actor, now);
        Touch();
    }

    public void Unfreeze(Guid actor, DateTimeOffset now)
    {
        if (!Frozen) return;
        Frozen = false;
        Record(Status, Status, "dispute closed", actor, now);
        Touch();
    }

    /// <summary>Pay the supplier releaseCents of what is held; the rest goes back to the shop.</summary>
    public Result Settle(long releaseCents, string reason, Guid? actor, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Held) return Result.Fail("payment.not_held", "There is no held money on this payment.");
        if (releaseCents < 0 || releaseCents > AmountCents) return Result.Fail("payment.release_invalid", "That is more than was paid.");
        ReleasedCents = releaseCents;
        RefundedCents = AmountCents - releaseCents;
        SettledAt = now;
        Frozen = false;
        var to = releaseCents > 0 ? PaymentStatus.Released : PaymentStatus.Refunded;
        Record(PaymentStatus.Held, to, $"{reason}: released {releaseCents}, refunded {RefundedCents}", actor, now);
        Status = to;
        Touch();
        return Result.Ok();
    }

    /// <summary>What to pay out when the delivery is simply accepted.</summary>
    public long AmountToRelease => DueCents ?? AmountCents;

    private void Record(PaymentStatus? from, PaymentStatus to, string reason, Guid? actor, DateTimeOffset now) =>
        _audit.Add(new PaymentAudit(Id, from, to, reason, actor, now));
}

/// <summary>M5: every payment state change, kept for good. Rows are only ever added.</summary>
public sealed class PaymentAudit : Entity
{
    private PaymentAudit() { }

    internal PaymentAudit(Guid paymentId, PaymentStatus? from, PaymentStatus to, string reason, Guid? actor, DateTimeOffset at)
    {
        PaymentId = paymentId;
        FromStatus = from;
        ToStatus = to;
        Reason = reason.Length > 500 ? reason[..500] : reason;
        ActorPersonId = actor;
        CreatedAt = at;
    }

    public Guid PaymentId { get; private set; }
    public PaymentStatus? FromStatus { get; private set; }
    public PaymentStatus ToStatus { get; private set; }
    public string Reason { get; private set; } = default!;
    public Guid? ActorPersonId { get; private set; }
}

/// <summary>A partner callback already handled. Callbacks arrive at least once, so the id of each is kept.</summary>
public sealed class WebhookReceipt : Entity
{
    private WebhookReceipt() { }

    public WebhookReceipt(string partner, string eventId, string type, DateTimeOffset at)
    {
        Partner = partner;
        EventId = eventId;
        Type = type;
        CreatedAt = at;
    }

    public string Partner { get; private set; } = default!;
    public string EventId { get; private set; } = default!;
    public string Type { get; private set; } = default!;
}
