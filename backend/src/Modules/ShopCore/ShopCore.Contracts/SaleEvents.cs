using TillKnowIT.SharedKernel;

namespace ShopCore.Contracts;

/// <summary>A sale was recorded for a customer. Customers awards loyalty points for it, once.</summary>
public sealed record SaleRecorded(Guid SaleId, Guid ShopId, Guid CustomerId, long TotalCents, DateTimeOffset SoldAt) : DomainEvent;

/// <summary>A sale was cancelled. Anything given for it, such as loyalty points, is taken back.</summary>
public sealed record SaleCancelled(Guid SaleId, Guid ShopId) : DomainEvent;

/// <summary>Goods from a sale were refunded. RefundCents of SaleTotalCents came back, so a share of what the sale earned is taken back too.</summary>
public sealed record SaleRefunded(Guid RefundId, Guid SaleId, Guid ShopId, long RefundCents, long SaleTotalCents) : DomainEvent;
