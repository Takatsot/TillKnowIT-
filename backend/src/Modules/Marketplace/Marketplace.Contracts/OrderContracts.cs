using TillKnowIT.SharedKernel;

namespace Marketplace.Contracts;

/// <summary>
/// For a supplier on the platform give SupplierProductId. For a supplier the shop added itself there is no catalogue,
/// so give Name (and Barcode, UnitsPerCase, CasePriceCents if known; an unknown price is recorded as 0 and agreed by the supplier).
/// </summary>
public sealed record OrderLineRequest(Guid? SupplierProductId, string? Name, string? Barcode, int Cases, int? UnitsPerCase, long? CasePriceCents);

/// <summary>PaymentMethod is on_delivery or in_app, and must be allowed by the supplier's terms.</summary>
public sealed record CreateOrderRequest(Guid CallerPersonId, Guid SupplierId, string PaymentMethod, List<OrderLineRequest> Lines);

public sealed record OrderSelection(Guid SupplierId, Guid SupplierProductId, int Cases);

/// <summary>M4-1: turn the chosen options from the order list into drafts, one order per supplier.</summary>
public sealed record BuildOrdersRequest(Guid CallerPersonId, string PaymentMethod, List<OrderSelection> Selections);

/// <summary>Channel is app, whatsapp or email. Whatsapp and email go to the supplier's own contact, which suits suppliers not on the platform.</summary>
public sealed record SendOrderRequest(Guid CallerPersonId, string Channel);

/// <summary>Action is confirm, change (needs Cases, optionally CasePriceCents) or decline.</summary>
public sealed record LineDecisionRequest(Guid LineId, string Action, int? Cases, long? CasePriceCents);

public sealed record RespondToOrderRequest(Guid CallerPersonId, List<LineDecisionRequest> Decisions, DateOnly? DeliveryDate);

/// <summary>DeliveredCases by line id. Lines left out were delivered in full.</summary>
public sealed record DeliverOrderRequest(Guid CallerPersonId, Dictionary<Guid, int>? DeliveredCases);

/// <summary>ReceivedCases by line id: what the shop counted. Lines left out are taken as delivered in full.</summary>
public sealed record ReceiveOrderRequest(Guid CallerPersonId, Dictionary<Guid, int>? ReceivedCases);

public sealed record OrderLineResponse(
    Guid Id, Guid? SupplierProductId, string Name, string? Barcode, int UnitsPerCase, int OrderedCases, int? ConfirmedCases, int? DeliveredCases,
    int EffectiveCases, long CasePriceCents, string Status);

/// <summary>Status is draft, sent, confirmed, delivered, cancelled or disputed. PaymentStatus is owing, held, paid or refunded.</summary>
public sealed record OrderResponse(
    Guid Id, Guid ShopId, Guid SupplierId, string SupplierName, string Reference, string Status, string Channel, DateOnly? DeliveryDate,
    string PaymentMethod, string PaymentStatus, List<OrderLineResponse> Lines, long SubtotalCents, long VatCents, long DeliveryFeeCents, long TotalCents,
    bool BelowMinimum, long MinOrderCents, bool HasChanges, DateTimeOffset? SentAt, DateTimeOffset? ConfirmedAt, DateTimeOffset? DeliveredAt, DateTimeOffset? ReceivedAt);

public sealed record BuiltOrder(OrderResponse Order, string? Warning);

public sealed record ReceivedLine(string Name, string? Barcode, int Units, long UnitCostCents);

/// <summary>
/// The shop received a delivery. ShopCore turns this into a stock-in review already filled in, so the owner only checks and confirms.
/// UnitCostCents includes VAT where the supplier's price left it out; the delivery fee is not in it.
/// </summary>
public sealed record OrderReceived(Guid OrderId, Guid ShopId, Guid SupplierId, string SupplierName, string Reference, List<ReceivedLine> Lines) : DomainEvent;
