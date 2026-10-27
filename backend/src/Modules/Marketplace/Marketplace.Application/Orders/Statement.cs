using Identity.Contracts;
using Marketplace.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.Reporting;
using TillKnowIT.SharedKernel;
using Marketplace.Application.Suppliers;

namespace Marketplace.Application.Orders;

/// <summary>
/// M5-4: one statement of a supplier's orders, what has been paid and what is still owing. Only orders that were actually sent count:
/// drafts are the shop's private business. An order is owing once it is delivered (or disputed) and not yet paid.
/// </summary>
public static class StatementBuilder
{
    public static ReportTable Build(string supplierName, IEnumerable<(PurchaseOrder Order, string ShopName)> orders, string period)
    {
        var rows = new List<object?[]>();
        long delivered = 0, paid = 0, held = 0, owing = 0;

        foreach (var (o, shop) in orders.Where(x => x.Order.Status != OrderStatus.Draft).OrderBy(x => x.Order.SentAt ?? x.Order.CreatedAt))
        {
            var billable = o.Status is OrderStatus.Delivered or OrderStatus.Disputed;
            var owes = billable && o.PaymentStatus == OrderPaymentStatus.Owing ? o.TotalCents : 0;
            if (billable)
            {
                delivered += o.TotalCents;
                if (o.PaymentStatus == OrderPaymentStatus.Paid) paid += o.TotalCents;
                if (o.PaymentStatus == OrderPaymentStatus.Held) held += o.TotalCents;
            }
            owing += owes;
            rows.Add([o.SentAt ?? o.CreatedAt, o.Reference, shop, o.Status.ToString().ToLowerInvariant(),
                o.PaymentMethod == OrderPaymentMethod.InApp ? "in the app" : "on delivery", o.PaymentStatus.ToString().ToLowerInvariant(),
                o.Status == OrderStatus.Cancelled ? 0L : o.TotalCents, owes]);
        }

        return new ReportTable($"Statement for {supplierName}",
            [new("Sent", ColumnKind.DateTime), new("Order"), new("Shop"), new("Order status"), new("Paid"), new("Payment status"), new("Total", ColumnKind.Money), new("Owing", ColumnKind.Money)],
            rows)
        {
            Notes = [$"Period: {period}", $"Delivered: R {CellText.Rand(delivered)}", $"Paid: R {CellText.Rand(paid)}", $"Held, to be released: R {CellText.Rand(held)}", $"Still owing: R {CellText.Rand(owing)}"],
        };
    }
}

/// <summary>From and To are the dates orders were sent, both included. Format is csv, xlsx or pdf.</summary>
public sealed record GetSupplierStatement(Guid SupplierId, Guid CallerPersonId, DateOnly? From, DateOnly? To, string? Format) : IQuery<ReportFile>;

internal sealed class GetSupplierStatementHandler(ISupplierStore suppliers, IOrderStore orders, IOrganisationAccess access, IShopDirectory shops, TimeProvider clock)
    : IQueryHandler<GetSupplierStatement, ReportFile>
{
    public async Task<Result<ReportFile>> Handle(GetSupplierStatement q, CancellationToken ct)
    {
        var supplier = await suppliers.Get(q.SupplierId, ct);
        if (supplier is null) return Result<ReportFile>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, q.CallerPersonId, "supplier.orders", ct);
        if (!allowed.IsSuccess) return Result<ReportFile>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (!ReportFormats.TryParse(q.Format, out var format)) return Result<ReportFile>.Fail("report.format_invalid", "Format must be csv, xlsx or pdf.");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var to = q.To ?? today;
        var from = q.From ?? to.AddDays(-89);
        if (from > to) return Result<ReportFile>.Fail("report.range_invalid", "The start date is after the end date.");
        if (to.DayNumber - from.DayNumber > 366) return Result<ReportFile>.Fail("report.range_too_large", "A statement covers at most a year.");

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var inRange = (await orders.ForSupplier(supplier.Id, null, ct)).Where(o => (o.SentAt ?? o.CreatedAt) >= start && (o.SentAt ?? o.CreatedAt) < end).ToList();

        var names = new Dictionary<Guid, string>();
        foreach (var shopId in inRange.Select(o => o.ShopId).Distinct()) names[shopId] = (await shops.Get(shopId, ct))?.Name ?? "Shop";

        var table = StatementBuilder.Build(supplier.Name, inRange.Select(o => (o, names[o.ShopId])), $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
        return Result<ReportFile>.Ok(ReportExporter.Export(table, format, $"statement-{from:yyyyMMdd}-{to:yyyyMMdd}"));
    }
}

/// <summary>An order paid on delivery: once the supplier has the money in hand, they mark it paid. In-app orders are paid by the Payments module.</summary>
public sealed record MarkOrderPaid(Guid OrderId, Guid CallerPersonId) : ICommand<Unit>;

internal sealed class MarkOrderPaidHandler(ISupplierStore suppliers, IOrderStore orders, IOrganisationAccess access) : ICommandHandler<MarkOrderPaid, Unit>
{
    public async Task<Result<Unit>> Handle(MarkOrderPaid c, CancellationToken ct)
    {
        var order = await orders.Get(c.OrderId, ct);
        if (order is null) return Result<Unit>.Fail("order.not_found", "Order not found.");
        var supplier = (await suppliers.Get(order.SupplierId, ct))!;
        var allowed = await SupplierAccess.Check(access, supplier, c.CallerPersonId, "supplier.orders", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);

        if (order.PaymentMethod != OrderPaymentMethod.OnDelivery) return Result<Unit>.Fail("order.paid_in_app", "This order is paid in the app.");
        if (order.Status is not (OrderStatus.Delivered or OrderStatus.Disputed)) return Result<Unit>.Fail("order.not_delivered", "Mark it paid once it has been delivered.");
        order.SetPaymentStatus(OrderPaymentStatus.Paid);
        await orders.Save(ct);
        return Result<Unit>.Ok(default);
    }
}
