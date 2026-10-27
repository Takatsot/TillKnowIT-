using TillKnowIT.SharedKernel;

namespace Marketplace.Contracts;

/// <summary>What the Payments module needs to know about an order. Status and PaymentStatus use the same words as OrderResponse.</summary>
public sealed record OrderPaymentInfo(
    Guid OrderId, Guid ShopId, Guid SupplierId, Guid? SupplierOrganisationId, string SupplierName, string Reference, string Status,
    string PaymentMethod, string PaymentStatus, long TotalCents, DateTimeOffset? DeliveredAt);

/// <summary>The order side of payments: Payments reads an order and moves its payment status; it never touches the order tables.</summary>
public interface IOrderPayments
{
    Task<OrderPaymentInfo?> Get(Guid orderId, CancellationToken ct);

    /// <summary>One of owing, held, paid or refunded.</summary>
    Task<Result> SetPaymentStatus(Guid orderId, string paymentStatus, CancellationToken ct);

    /// <summary>A dispute freezes a delivered order; clearing it puts the order back to delivered.</summary>
    Task<Result> SetDisputed(Guid orderId, bool disputed, CancellationToken ct);
}

/// <summary>
/// The goods were delivered: marked so by the supplier (ByShop false) or counted and confirmed by the shop (ByShop true).
/// TotalCents is what the shop owes for what actually arrived. Payments releases held money on this.
/// </summary>
public sealed record OrderDelivered(Guid OrderId, Guid ShopId, Guid SupplierId, long TotalCents, bool ByShop) : DomainEvent;
