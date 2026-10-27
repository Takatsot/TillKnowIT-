using Marketplace.Contracts;
using Marketplace.Application.Suppliers;
using Marketplace.Domain;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Orders;

/// <summary>The order side of in-app payments (M5): lets the Payments module read an order and move its payment status.</summary>
public sealed class OrderPaymentsService(IOrderStore orders, ISupplierStore suppliers) : IOrderPayments
{
    public async Task<OrderPaymentInfo?> Get(Guid orderId, CancellationToken ct)
    {
        var o = await orders.Get(orderId, ct);
        if (o is null) return null;
        var supplier = await suppliers.Get(o.SupplierId, ct);
        return new OrderPaymentInfo(
            o.Id, o.ShopId, o.SupplierId, supplier?.OrganisationId, supplier?.Name ?? "Supplier", o.Reference, o.Status.ToString().ToLowerInvariant(),
            o.PaymentMethod == OrderPaymentMethod.InApp ? "in_app" : "on_delivery", o.PaymentStatus.ToString().ToLowerInvariant(), o.TotalCents, o.DeliveredAt);
    }

    public async Task<Result> SetPaymentStatus(Guid orderId, string paymentStatus, CancellationToken ct)
    {
        var o = await orders.Get(orderId, ct);
        if (o is null) return Result.Fail("order.not_found", "Order not found.");
        if (!Enum.TryParse<OrderPaymentStatus>(paymentStatus, ignoreCase: true, out var status)) return Result.Fail("order.payment_status_invalid", "Payment status is owing, held, paid or refunded.");
        o.SetPaymentStatus(status);
        await orders.Save(ct);
        return Result.Ok();
    }

    public async Task<Result> SetDisputed(Guid orderId, bool disputed, CancellationToken ct)
    {
        var o = await orders.Get(orderId, ct);
        if (o is null) return Result.Fail("order.not_found", "Order not found.");
        if (disputed)
        {
            var marked = o.MarkDisputed();
            if (!marked.IsSuccess) return marked;
        }
        else o.ClearDispute();
        await orders.Save(ct);
        return Result.Ok();
    }
}
