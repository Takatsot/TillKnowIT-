using Marketplace.Contracts;
using TillKnowIT.SharedKernel;

namespace Marketplace.Domain;

public enum OrderStatus { Draft, Sent, Confirmed, Delivered, Cancelled, Disputed }

public enum OrderChannel { App, Whatsapp, Email }

public enum OrderPaymentMethod { OnDelivery, InApp }

/// <summary>Owing: nothing paid yet. Held: paid in the app and held until delivery is confirmed. Paid: released or paid on delivery.</summary>
public enum OrderPaymentStatus { Owing, Held, Paid, Refunded }

public enum LineStatus { Pending, Confirmed, Changed, Declined }

public sealed class PurchaseOrderLine
{
    private PurchaseOrderLine() { }

    public PurchaseOrderLine(Guid? supplierProductId, string name, string? barcode, int unitsPerCase, int orderedCases, long casePriceCents, bool vatIncluded)
    {
        Id = Guid.CreateVersion7();
        SupplierProductId = supplierProductId;
        Name = name;
        Barcode = barcode;
        UnitsPerCase = unitsPerCase;
        OrderedCases = orderedCases;
        CasePriceCents = casePriceCents;
        VatIncluded = vatIncluded;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }

    /// <summary>Null for a line the shop typed for a supplier that is not on the platform.</summary>
    public Guid? SupplierProductId { get; private set; }

    /// <summary>The name and price as they were when ordered, so later catalogue changes never rewrite an order.</summary>
    public string Name { get; private set; } = default!;
    public string? Barcode { get; private set; }
    public int UnitsPerCase { get; private set; }
    public int OrderedCases { get; private set; }
    public long CasePriceCents { get; private set; }
    public bool VatIncluded { get; private set; }
    public LineStatus Status { get; private set; } = LineStatus.Pending;
    public int? ConfirmedCases { get; private set; }
    public int? DeliveredCases { get; private set; }

    /// <summary>The cases that count right now: what was delivered, else what the supplier confirmed, else what was asked for.</summary>
    public int EffectiveCases => Status == LineStatus.Declined ? 0 : DeliveredCases ?? ConfirmedCases ?? OrderedCases;

    internal void Attach(Guid orderId) => OrderId = orderId;

    internal void Confirm() { Status = LineStatus.Confirmed; ConfirmedCases = OrderedCases; }

    internal void Change(int cases, long? casePriceCents)
    {
        Status = LineStatus.Changed;
        ConfirmedCases = cases;
        if (casePriceCents is { } p) CasePriceCents = p;
    }

    internal void Decline() { Status = LineStatus.Declined; ConfirmedCases = 0; }

    internal void SetDelivered(int cases) => DeliveredCases = cases;
}

/// <summary>What the supplier decided for one line.</summary>
public sealed record LineDecision(Guid LineId, string Action, int? Cases, long? CasePriceCents);

public sealed class PurchaseOrder : VersionedEntity
{
    public const int VatPercent = 15;
    public const int MaxDeliveryDaysAhead = 30;

    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder() { }

    public static Result<PurchaseOrder> Create(Guid shopId, Guid supplierId, Guid createdBy, IEnumerable<PurchaseOrderLine> lines, OrderPaymentMethod payment,
        long minOrderCents, long deliveryFeeCents, DateTimeOffset now)
    {
        var list = lines.ToList();
        if (list.Count == 0) return Result<PurchaseOrder>.Fail("order.no_lines", "An order needs at least one item.");
        foreach (var l in list)
        {
            if (string.IsNullOrWhiteSpace(l.Name)) return Result<PurchaseOrder>.Fail("order.line_name", "Every item needs a name.");
            if (l.OrderedCases is < 1 or > 10_000) return Result<PurchaseOrder>.Fail("order.line_quantity", "Order between 1 and 10,000 cases of each item.");
            if (l.UnitsPerCase is < 1 or > 10_000) return Result<PurchaseOrder>.Fail("order.line_case_size", "A case holds between 1 and 10,000 units.");
            if (l.CasePriceCents < 0) return Result<PurchaseOrder>.Fail("order.line_price", "A price cannot be negative.");
        }
        if (list.Select(l => l.SupplierProductId).Where(id => id is not null).GroupBy(id => id).Any(g => g.Count() > 1))
            return Result<PurchaseOrder>.Fail("order.duplicate_line", "Each product appears once; add the cases together.");

        var order = new PurchaseOrder { ShopId = shopId, SupplierId = supplierId, PaymentMethod = payment, MinOrderCents = minOrderCents, DeliveryFeeCents = deliveryFeeCents };
        order.StampFromDevice(Guid.CreateVersion7(), now, createdBy, null);
        foreach (var l in list) { l.Attach(order.Id); order._lines.Add(l); }
        return Result<PurchaseOrder>.Ok(order);
    }

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;
    public Guid ShopId { get; private set; }
    public Guid SupplierId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public OrderChannel Channel { get; private set; } = OrderChannel.App;
    public DateOnly? DeliveryDate { get; private set; }
    public OrderPaymentMethod PaymentMethod { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.Owing;
    public long MinOrderCents { get; private set; }
    public long DeliveryFeeCents { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }

    // ---- money ----

    /// <summary>The listed price of the cases that count, before VAT and delivery.</summary>
    public long SubtotalCents => _lines.Sum(l => l.CasePriceCents * l.EffectiveCases);

    /// <summary>VAT is added only on lines whose price leaves it out.</summary>
    public long VatCents => _lines.Where(l => !l.VatIncluded).Sum(l => RoundHalfUp(l.CasePriceCents * l.EffectiveCases * VatPercent / 100m));

    /// <summary>What the shop pays: listed prices, VAT where it is extra, and the delivery fee (not charged when nothing is being delivered).</summary>
    public long TotalCents => SubtotalCents == 0 ? 0 : SubtotalCents + VatCents + DeliveryFeeCents;

    /// <summary>True when the order is below the supplier's minimum. A warning for the owner, not a block.</summary>
    public bool BelowMinimum => SubtotalCents < MinOrderCents;

    private static long RoundHalfUp(decimal v) => (long)Math.Floor(v + 0.5m);

    // ---- lifecycle ----

    /// <summary>Drafts can be changed until sent.</summary>
    public Result Cancel()
    {
        if (Status is not (OrderStatus.Draft or OrderStatus.Sent))
            return Result.Fail("order.cannot_cancel", "An order can only be cancelled before the supplier confirms it.");
        Status = OrderStatus.Cancelled;
        Touch();
        return Result.Ok();
    }

    /// <summary>M4-2: send the order by app, WhatsApp or email. The supplier must be reachable that way.</summary>
    public Result Send(OrderChannel channel, DateTimeOffset now)
    {
        if (Status != OrderStatus.Draft) return Result.Fail("order.already_sent", "This order has already been sent.");
        Channel = channel;
        Status = OrderStatus.Sent;
        SentAt = now;
        Touch();
        return Result.Ok();
    }

    public void ChoosePayment(OrderPaymentMethod method)
    {
        if (Status == OrderStatus.Draft) PaymentMethod = method;
    }

    /// <summary>M4-3: the supplier confirms, changes or declines every line and sets a delivery date.</summary>
    public Result Respond(IReadOnlyCollection<LineDecision> decisions, DateOnly? deliveryDate, DateOnly today, DateTimeOffset now)
    {
        if (Status != OrderStatus.Sent) return Result.Fail("order.not_awaiting_reply", "This order is not waiting for a reply.");
        if (decisions.Select(d => d.LineId).Distinct().Count() != _lines.Count || decisions.Any(d => _lines.All(l => l.Id != d.LineId)))
            return Result.Fail("order.decision_missing", "Reply to every line of the order.");

        foreach (var d in decisions)
        {
            switch (d.Action)
            {
                case "confirm": case "decline": break;
                case "change":
                    if (d.Cases is null or < 1 or > 10_000) return Result.Fail("order.change_invalid", "A changed line needs between 1 and 10,000 cases. To refuse a line, decline it.");
                    if (d.CasePriceCents is < 0) return Result.Fail("order.change_invalid", "A price cannot be negative.");
                    break;
                default: return Result.Fail("order.action_invalid", "Each line is confirm, change or decline.");
            }
        }

        var anyAccepted = decisions.Any(d => d.Action != "decline");
        if (anyAccepted)
        {
            if (deliveryDate is null) return Result.Fail("order.delivery_date_required", "Say when you will deliver.");
            if (deliveryDate <= today || deliveryDate > today.AddDays(MaxDeliveryDaysAhead))
                return Result.Fail("order.delivery_date_invalid", $"Delivery must be from tomorrow to {MaxDeliveryDaysAhead} days ahead.");
        }

        foreach (var d in decisions)
        {
            var line = _lines.Single(l => l.Id == d.LineId);
            switch (d.Action)
            {
                case "confirm": line.Confirm(); break;
                case "change": line.Change(d.Cases!.Value, d.CasePriceCents); break;
                default: line.Decline(); break;
            }
        }

        if (anyAccepted)
        {
            Status = OrderStatus.Confirmed;
            DeliveryDate = deliveryDate;
            ConfirmedAt = now;
        }
        else
        {
            Status = OrderStatus.Cancelled;
        }
        Touch();
        return Result.Ok();
    }

    /// <summary>True when the supplier changed or declined anything, so the shop should be told what is different.</summary>
    public bool HasChangesFromOrder => _lines.Any(l => l.Status is LineStatus.Changed or LineStatus.Declined);

    /// <summary>M4-4: the supplier marks the goods delivered, with what was actually on the truck.</summary>
    public Result MarkDelivered(IReadOnlyDictionary<Guid, int> deliveredCasesByLine, DateTimeOffset now)
    {
        if (Status != OrderStatus.Confirmed) return Result.Fail("order.not_confirmed", "Only a confirmed order can be delivered.");
        return ApplyDelivered(deliveredCasesByLine, now, supplier: true);
    }

    /// <summary>
    /// M4-4: the shop confirms what arrived. A short delivery adjusts the order to what was really received, so the shop
    /// is only charged for that. Without a count, what the supplier marked as delivered stands.
    /// </summary>
    public Result Receive(IReadOnlyDictionary<Guid, int>? receivedCasesByLine, DateTimeOffset now)
    {
        if (Status != OrderStatus.Delivered && Status != OrderStatus.Confirmed)
            return Result.Fail("order.not_delivered", "This order has not been delivered yet.");
        if (ReceivedAt is not null) return Result.Fail("order.already_received", "This delivery has already been received.");

        if (receivedCasesByLine is { Count: > 0 })
        {
            var applied = ApplyDelivered(receivedCasesByLine, now, supplier: false);
            if (!applied.IsSuccess) return applied;
        }

        // Received before the supplier marked it delivered: lines the shop did not count are taken as delivered in full.
        if (Status == OrderStatus.Confirmed)
        {
            foreach (var l in _lines.Where(l => l.Status != LineStatus.Declined && l.DeliveredCases is null)) l.SetDelivered(l.ConfirmedCases ?? l.OrderedCases);
            Status = OrderStatus.Delivered;
            DeliveredAt = now;
        }

        ReceivedAt = now;
        Raise(new OrderDelivered(Id, ShopId, SupplierId, TotalCents, ByShop: true));
        Touch();
        return Result.Ok();
    }

    private Result ApplyDelivered(IReadOnlyDictionary<Guid, int> cases, DateTimeOffset now, bool supplier)
    {
        foreach (var (lineId, count) in cases)
        {
            var line = _lines.FirstOrDefault(l => l.Id == lineId);
            if (line is null || line.Status == LineStatus.Declined) return Result.Fail("order.line_unknown", "That line is not part of this order.");
            var ceiling = line.ConfirmedCases ?? line.OrderedCases;
            if (count < 0 || count > ceiling) return Result.Fail("order.delivered_invalid", $"{line.Name}: delivered cases must be from 0 to {ceiling}.");
        }
        foreach (var (lineId, count) in cases) _lines.Single(l => l.Id == lineId).SetDelivered(count);

        if (supplier)
        {
            // Lines the supplier did not mention were delivered in full.
            foreach (var l in _lines.Where(l => l.Status != LineStatus.Declined && l.DeliveredCases is null)) l.SetDelivered(l.ConfirmedCases ?? l.OrderedCases);
            Status = OrderStatus.Delivered;
            DeliveredAt = now;
            Raise(new OrderDelivered(Id, ShopId, SupplierId, TotalCents, ByShop: false));
        }
        Touch();
        return Result.Ok();
    }

    /// <summary>Short reference for people and messages, from the order id.</summary>
    public string Reference => Id.ToString("N")[..8].ToUpperInvariant();

    /// <summary>
    /// Tells ShopCore what arrived, so it can prefill the stock-in review. Units and cost are per unit, worked out from cases;
    /// VAT is added to the cost where the supplier's price left it out.
    /// </summary>
    public void AnnounceReceived(string supplierName)
    {
        var lines = _lines.Where(l => l.EffectiveCases > 0).Select(l => new ReceivedLine(
            l.Name, l.Barcode, l.EffectiveCases * l.UnitsPerCase,
            RoundHalfUp((l.VatIncluded ? l.CasePriceCents : l.CasePriceCents * (100m + VatPercent) / 100m) / l.UnitsPerCase))).ToList();
        Raise(new OrderReceived(Id, ShopId, SupplierId, supplierName, Reference, lines));
    }

    public Result MarkDisputed()
    {
        if (Status == OrderStatus.Disputed) return Result.Ok();
        if (Status != OrderStatus.Delivered) return Result.Fail("order.not_delivered", "Only a delivered order can be disputed.");
        Status = OrderStatus.Disputed;
        Touch();
        return Result.Ok();
    }

    public void ClearDispute()
    {
        if (Status == OrderStatus.Disputed) { Status = OrderStatus.Delivered; Touch(); }
    }

    public void SetPaymentStatus(OrderPaymentStatus status)
    {
        PaymentStatus = status;
        Touch();
    }
}
