using Identity.Contracts;
using Marketplace.Contracts;
using Payments.Contracts;
using Payments.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Payments.Application;

internal static class DisputeMapping
{
    public static DisputeResponse ToResponse(Dispute d) => new(
        d.Id, d.OrderId, d.ShopId, d.SupplierId, d.RaisedBy.ToString().ToLowerInvariant(), d.Reason, d.Status.ToString().ToLowerInvariant(),
        d.Outcome is null ? null : (d.Outcome == DisputeOutcome.PaySupplier ? "pay_supplier" : "refund_shop"), d.Note, d.CreatedAt, d.ResolvedAt);

    public static bool TryParseOutcome(string? text, out DisputeOutcome outcome)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "pay_supplier": outcome = DisputeOutcome.PaySupplier; return true;
            case "refund_shop": outcome = DisputeOutcome.RefundShop; return true;
            default: outcome = default; return false;
        }
    }
}

// ---------------------------------------------------------------- M5-3: disputes

public sealed record RaiseDispute(Guid OrderId, RaiseDisputeRequest Request) : ICommand<DisputeResponse>;

internal sealed class RaiseDisputeHandler(IOrderPayments orders, IPaymentStore payments, IDisputeStore disputes, IOrganisationAccess access, TimeProvider clock)
    : ICommandHandler<RaiseDispute, DisputeResponse>
{
    /// <summary>After this long a delivered order can no longer be disputed.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    public async Task<Result<DisputeResponse>> Handle(RaiseDispute c, CancellationToken ct)
    {
        var order = await orders.Get(c.OrderId, ct);
        if (order is null) return Result<DisputeResponse>.Fail("order.not_found", "Order not found.");

        var caller = c.Request.CallerPersonId;
        DisputeSide side;
        if (await access.Has(caller, order.ShopId, "stock.receive", ct)) side = DisputeSide.Shop;
        else if (order.SupplierOrganisationId is { } org && await access.Has(caller, org, "supplier.orders", ct)) side = DisputeSide.Supplier;
        else return Result<DisputeResponse>.Fail("permission.denied", "You do not have access to this order.");

        if (await disputes.OpenForOrder(order.OrderId, ct) is not null) return Result<DisputeResponse>.Fail("dispute.already_open", "This order already has an open dispute.");
        if (order.Status != "delivered") return Result<DisputeResponse>.Fail("dispute.order_not_disputable", "Only a delivered order can be disputed.");
        var now = clock.GetUtcNow();
        if (order.DeliveredAt is { } at && now - at > Window) return Result<DisputeResponse>.Fail("dispute.window_closed", "The 14 days to dispute this delivery have passed.");

        var raised = Dispute.Raise(order.OrderId, order.ShopId, order.SupplierId, caller, side, c.Request.Reason, now);
        if (!raised.IsSuccess) return Result<DisputeResponse>.Fail(raised.Error!.Code, raised.Error.Message);

        var marked = await orders.SetDisputed(order.OrderId, true, ct);
        if (!marked.IsSuccess) return Result<DisputeResponse>.Fail(marked.Error!.Code, marked.Error.Message);

        // Held money stops moving until the admin decides.
        var payment = await payments.ForOrder(order.OrderId, ct);
        if (payment is { Status: PaymentStatus.Held }) payment.Freeze(caller, now);

        await disputes.Add(raised.Value!, ct);
        await disputes.Save(ct);
        if (payment is not null) await payments.Save(ct);
        return Result<DisputeResponse>.Ok(DisputeMapping.ToResponse(raised.Value!));
    }
}

public sealed record ResolveDispute(Guid DisputeId, ResolveDisputeRequest Request) : ICommand<DisputeResponse>;

internal sealed class ResolveDisputeHandler(IDisputeStore disputes, IPaymentStore payments, PaymentSettlement settlement, IOrderPayments orders, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<ResolveDispute, DisputeResponse>
{
    public async Task<Result<DisputeResponse>> Handle(ResolveDispute c, CancellationToken ct)
    {
        if (!await permissions.Has(c.Request.CallerPersonId, "dispute.resolve", ct)) return Result<DisputeResponse>.Fail("permission.denied", "Only a platform admin can resolve a dispute.");
        if (!DisputeMapping.TryParseOutcome(c.Request.Outcome, out var outcome)) return Result<DisputeResponse>.Fail("dispute.outcome_invalid", "The outcome is pay_supplier or refund_shop.");
        var dispute = await disputes.Get(c.DisputeId, ct);
        if (dispute is null) return Result<DisputeResponse>.Fail("dispute.not_found", "Dispute not found.");

        var now = clock.GetUtcNow();
        var admin = c.Request.CallerPersonId;
        var can = dispute.CanResolve(c.Request.Note);
        if (!can.IsSuccess) return Result<DisputeResponse>.Fail(can.Error!.Code, can.Error.Message);

        // Money first: if the partner cannot pay or refund now, nothing has changed and the dispute stays open to try again.
        var payment = await payments.ForOrder(dispute.OrderId, ct);
        if (payment is { Status: PaymentStatus.Held })
        {
            var release = outcome == DisputeOutcome.PaySupplier ? payment.AmountToRelease : 0;
            var settled = await settlement.Settle(payment, release, $"dispute resolved ({c.Request.Outcome!.Trim().ToLowerInvariant()})", admin, now, ct);
            if (!settled.IsSuccess) return Result<DisputeResponse>.Fail(settled.Error!.Code, settled.Error.Message);
        }

        dispute.Resolve(outcome, c.Request.Note, admin, now);
        var cleared = await orders.SetDisputed(dispute.OrderId, false, ct);
        if (!cleared.IsSuccess) return Result<DisputeResponse>.Fail(cleared.Error!.Code, cleared.Error.Message);
        await disputes.Save(ct);
        return Result<DisputeResponse>.Ok(DisputeMapping.ToResponse(dispute));
    }
}

public sealed record ListOpenDisputes(Guid CallerPersonId) : IQuery<List<DisputeResponse>>;

internal sealed class ListOpenDisputesHandler(IDisputeStore disputes, IPermissionChecker permissions) : IQueryHandler<ListOpenDisputes, List<DisputeResponse>>
{
    public async Task<Result<List<DisputeResponse>>> Handle(ListOpenDisputes q, CancellationToken ct)
    {
        if (!await permissions.Has(q.CallerPersonId, "dispute.resolve", ct)) return Result<List<DisputeResponse>>.Fail("permission.denied", "Only a platform admin can see open disputes.");
        return Result<List<DisputeResponse>>.Ok((await disputes.Open(ct)).Select(DisputeMapping.ToResponse).ToList());
    }
}
