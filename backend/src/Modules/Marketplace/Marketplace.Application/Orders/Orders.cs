using System.Globalization;
using Identity.Contracts;
using Marketplace.Application.Offers;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using Messaging.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Orders;

public interface IOrderStore
{
    Task Add(PurchaseOrder order, CancellationToken ct);
    Task<PurchaseOrder?> Get(Guid orderId, CancellationToken ct);
    Task<List<PurchaseOrder>> ForShop(Guid shopId, CancellationToken ct);
    Task<List<PurchaseOrder>> ForSupplier(Guid supplierId, OrderStatus? status, CancellationToken ct);
    Task<LastPrice?> GetLastPrice(Guid shopId, Guid supplierId, string barcode, CancellationToken ct);
    Task Add(LastPrice price, CancellationToken ct);
    Task Save(CancellationToken ct);
}

internal static class Rands
{
    public static string Rand(long cents) => "R" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}

internal static class OrderMapping
{
    public static OrderResponse ToResponse(PurchaseOrder o, string supplierName) => new(
        o.Id, o.ShopId, o.SupplierId, supplierName, o.Reference, Lower(o.Status), Lower(o.Channel), o.DeliveryDate,
        o.PaymentMethod == OrderPaymentMethod.InApp ? "in_app" : "on_delivery", Lower(o.PaymentStatus),
        o.Lines.Select(l => new OrderLineResponse(l.Id, l.SupplierProductId, l.Name, l.Barcode, l.UnitsPerCase, l.OrderedCases, l.ConfirmedCases, l.DeliveredCases,
            l.EffectiveCases, l.CasePriceCents, Lower(l.Status))).ToList(),
        o.SubtotalCents, o.VatCents, o.DeliveryFeeCents, o.TotalCents, o.BelowMinimum, o.MinOrderCents, o.HasChangesFromOrder,
        o.SentAt, o.ConfirmedAt, o.DeliveredAt, o.ReceivedAt);

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    public static bool TryParsePayment(string? text, out OrderPaymentMethod method)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "on_delivery": method = OrderPaymentMethod.OnDelivery; return true;
            case "in_app": method = OrderPaymentMethod.InApp; return true;
            default: method = default; return false;
        }
    }

    public static bool TryParseChannel(string? text, out OrderChannel channel)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "app": channel = OrderChannel.App; return true;
            case "whatsapp": channel = OrderChannel.Whatsapp; return true;
            case "email": channel = OrderChannel.Email; return true;
            default: channel = default; return false;
        }
    }
}

/// <summary>Messages about orders: to a supplier by WhatsApp or email, and to the shop's owner about changes. Each has a dedupe key.</summary>
public sealed class OrderNotifier(IShopDirectory shops, IMessageQueue queue)
{
    public async Task<Result<QueueMessageResult>> ToSupplier(Supplier supplier, OrderChannel channel, string template, Dictionary<string, string> values, string dedupeKey, CancellationToken ct)
    {
        var (name, destination) = channel == OrderChannel.Whatsapp ? ("whatsapp", supplier.Whatsapp) : ("email", supplier.Email);
        if (destination is null) return Result<QueueMessageResult>.Fail("order.supplier_unreachable", $"This supplier has no {name} contact.");
        return await queue.Queue(new QueueMessageRequest(template, "en", name, destination, values, null, dedupeKey), ct);
    }

    public async Task ToShopOwner(Guid shopId, string template, Dictionary<string, string> values, string dedupeKey, CancellationToken ct)
    {
        var shop = await shops.Get(shopId, ct);
        var (channel, destination) = shop switch
        {
            { OwnerPhone: { Length: > 0 } phone } => ("whatsapp", phone),
            { OwnerEmail: { Length: > 0 } email } => ("email", email),
            _ => ("", ""),
        };
        if (channel.Length == 0) return;   // no contact on file: the owner sees the change in the app instead
        values["shop"] = shop!.Name;
        await queue.Queue(new QueueMessageRequest(template, shop.OwnerLanguage, channel, destination, values, shopId, dedupeKey), ct);
    }
}

internal static class OrderText
{
    public static string Lines(PurchaseOrder o) => string.Join("\n", o.Lines.Select(l =>
        $"{l.OrderedCases} x {l.Name} ({l.UnitsPerCase} per case)" + (l.CasePriceCents > 0 ? $" @ {Rands.Rand(l.CasePriceCents)} a case" : "")));
}

// ---------------------------------------------------------------- M4-1: create and build

public sealed record CreateOrder(Guid ShopId, CreateOrderRequest Request) : ICommand<OrderResponse>;

public sealed record BuildOrders(Guid ShopId, BuildOrdersRequest Request) : ICommand<List<BuiltOrder>>;

internal sealed class CreateOrderHandlers(ISupplierStore suppliers, ISupplierProductStore products, IOrderStore orders, IOrganisationAccess access, TimeProvider clock)
    : ICommandHandler<CreateOrder, OrderResponse>, ICommandHandler<BuildOrders, List<BuiltOrder>>
{
    public Task<Result<OrderResponse>> Handle(CreateOrder c, CancellationToken ct) =>
        Create(c.ShopId, c.Request.CallerPersonId, c.Request.SupplierId, c.Request.PaymentMethod, c.Request.Lines, ct);

    public async Task<Result<List<BuiltOrder>>> Handle(BuildOrders c, CancellationToken ct)
    {
        var r = c.Request;
        if (r.Selections is null || r.Selections.Count == 0) return Result<List<BuiltOrder>>.Fail("order.no_lines", "Choose at least one item to order.");

        // One order per supplier.
        var built = new List<BuiltOrder>();
        foreach (var group in r.Selections.GroupBy(s => s.SupplierId))
        {
            var lines = group.Select(s => new OrderLineRequest(s.SupplierProductId, null, null, s.Cases, null, null)).ToList();
            var result = await Create(c.ShopId, r.CallerPersonId, group.Key, r.PaymentMethod, lines, ct);
            if (!result.IsSuccess) return Result<List<BuiltOrder>>.Fail(result.Error!.Code, result.Error.Message);
            var order = result.Value!;
            built.Add(new BuiltOrder(order, order.BelowMinimum
                ? $"{order.SupplierName} wants at least {Rands.Rand(order.MinOrderCents)}; this order is {Rands.Rand(order.SubtotalCents)}."
                : null));
        }
        return Result<List<BuiltOrder>>.Ok(built);
    }

    private async Task<Result<OrderResponse>> Create(Guid shopId, Guid caller, Guid supplierId, string paymentText, List<OrderLineRequest>? requested, CancellationToken ct)
    {
        if (!await access.Has(caller, shopId, "stock.receive", ct)) return Result<OrderResponse>.Fail("permission.denied", "You do not have access to this shop.");
        if (!OrderMapping.TryParsePayment(paymentText, out var payment)) return Result<OrderResponse>.Fail("order.payment_invalid", "Payment is on_delivery or in_app.");

        var supplier = await suppliers.Get(supplierId, ct);
        var forThisShop = supplier is not null && supplier.Status == SupplierStatus.Unlisted && supplier.CreatedByShopId == shopId;
        if (supplier is null || !(supplier.IsTrading || forThisShop)) return Result<OrderResponse>.Fail("supplier.not_found", "Supplier not found.");

        // A supplier's terms say how the shop may pay. A supplier off the platform can only be paid on delivery.
        var allowed = forThisShop ? payment == OrderPaymentMethod.OnDelivery
            : supplier.PaymentTerms == PaymentTerms.Both || (supplier.PaymentTerms == PaymentTerms.OnDelivery) == (payment == OrderPaymentMethod.OnDelivery);
        if (!allowed) return Result<OrderResponse>.Fail("order.payment_not_offered", "This supplier does not offer that way of paying.");

        var lines = new List<PurchaseOrderLine>();
        foreach (var l in requested ?? [])
        {
            if (forThisShop)
            {
                if (string.IsNullOrWhiteSpace(l.Name)) return Result<OrderResponse>.Fail("order.line_name", "Every item needs a name.");
                lines.Add(new PurchaseOrderLine(null, l.Name.Trim(), string.IsNullOrWhiteSpace(l.Barcode) ? null : l.Barcode.Trim(), l.UnitsPerCase ?? 1, l.Cases, l.CasePriceCents ?? 0, true));
                continue;
            }

            var product = l.SupplierProductId is { } id ? await products.Get(id, ct) : null;
            if (product is null || product.SupplierId != supplier.Id) return Result<OrderResponse>.Fail("order.product_unknown", "An item is not in this supplier's catalogue.");
            if (!product.InStock) return Result<OrderResponse>.Fail("order.out_of_stock", $"{product.Name} is out of stock.");
            lines.Add(new PurchaseOrderLine(product.Id, product.Name, product.Barcode, product.UnitsPerCase, l.Cases, product.CasePriceCents, product.VatIncluded));
        }

        var created = PurchaseOrder.Create(shopId, supplier.Id, caller, lines, payment, supplier.MinOrderCents, supplier.DeliveryFeeCents, clock.GetUtcNow());
        if (!created.IsSuccess) return Result<OrderResponse>.Fail(created.Error!.Code, created.Error.Message);

        await orders.Add(created.Value!, ct);
        await orders.Save(ct);
        return Result<OrderResponse>.Ok(OrderMapping.ToResponse(created.Value!, supplier.Name));
    }
}

// ---------------------------------------------------------------- M4-2: send

public sealed record SendOrder(Guid OrderId, SendOrderRequest Request) : ICommand<OrderResponse>;

public sealed record CancelOrder(Guid OrderId, Guid CallerPersonId) : ICommand<Unit>;

internal sealed class SendOrderHandlers(ISupplierStore suppliers, IOrderStore orders, IOrganisationAccess access, IShopDirectory shops, OrderNotifier notifier, TimeProvider clock)
    : ICommandHandler<SendOrder, OrderResponse>, ICommandHandler<CancelOrder, Unit>
{
    public async Task<Result<OrderResponse>> Handle(SendOrder c, CancellationToken ct)
    {
        var order = await orders.Get(c.OrderId, ct);
        if (order is null) return Result<OrderResponse>.Fail("order.not_found", "Order not found.");
        if (!await access.Has(c.Request.CallerPersonId, order.ShopId, "stock.receive", ct)) return Result<OrderResponse>.Fail("permission.denied", "You do not have access to this order.");
        if (!OrderMapping.TryParseChannel(c.Request.Channel, out var channel)) return Result<OrderResponse>.Fail("order.channel_invalid", "Channel is app, whatsapp or email.");

        var supplier = (await suppliers.Get(order.SupplierId, ct))!;
        // The app channel needs a supplier on the platform; WhatsApp and email need that contact on file.
        if (channel == OrderChannel.App && !supplier.IsTrading) return Result<OrderResponse>.Fail("order.supplier_not_on_app", "This supplier is not on the app. Send the order by WhatsApp or email.");
        if (channel == OrderChannel.Whatsapp && supplier.Whatsapp is null) return Result<OrderResponse>.Fail("order.supplier_unreachable", "This supplier has no WhatsApp number.");
        if (channel == OrderChannel.Email && supplier.Email is null) return Result<OrderResponse>.Fail("order.supplier_unreachable", "This supplier has no email address.");

        var sent = order.Send(channel, clock.GetUtcNow());
        if (!sent.IsSuccess) return Result<OrderResponse>.Fail(sent.Error!.Code, sent.Error.Message);

        if (channel != OrderChannel.App)
        {
            var shop = await shops.Get(order.ShopId, ct);
            var message = await notifier.ToSupplier(supplier, channel, "order.new", new Dictionary<string, string>
            {
                ["shop"] = shop?.Name ?? "A shop",
                ["order"] = order.Reference,
                ["lines"] = OrderText.Lines(order),
                ["payment"] = order.PaymentMethod == OrderPaymentMethod.InApp ? "paid through the app" : "payable on delivery",
            }, $"order:{order.Id:N}:new", ct);
            // If the message cannot even be queued (a bad number), the order must not look sent.
            if (!message.IsSuccess) return Result<OrderResponse>.Fail(message.Error!.Code, message.Error.Message);
        }

        await orders.Save(ct);
        return Result<OrderResponse>.Ok(OrderMapping.ToResponse(order, supplier.Name));
    }

    public async Task<Result<Unit>> Handle(CancelOrder c, CancellationToken ct)
    {
        var order = await orders.Get(c.OrderId, ct);
        if (order is null) return Result<Unit>.Fail("order.not_found", "Order not found.");
        if (!await access.Has(c.CallerPersonId, order.ShopId, "stock.receive", ct)) return Result<Unit>.Fail("permission.denied", "You do not have access to this order.");
        var cancelled = order.Cancel();
        if (!cancelled.IsSuccess) return Result<Unit>.Fail(cancelled.Error!.Code, cancelled.Error.Message);
        await orders.Save(ct);
        return Result<Unit>.Ok(default);
    }
}

// ---------------------------------------------------------------- M4-3: the supplier replies

public sealed record RespondToOrder(Guid OrderId, RespondToOrderRequest Request) : ICommand<OrderResponse>;

internal sealed class RespondToOrderHandler(ISupplierStore suppliers, IOrderStore orders, IOrganisationAccess access, OrderNotifier notifier, TimeProvider clock)
    : ICommandHandler<RespondToOrder, OrderResponse>
{
    public async Task<Result<OrderResponse>> Handle(RespondToOrder c, CancellationToken ct)
    {
        var order = await orders.Get(c.OrderId, ct);
        if (order is null) return Result<OrderResponse>.Fail("order.not_found", "Order not found.");
        var supplier = (await suppliers.Get(order.SupplierId, ct))!;
        var allowed = await SupplierAccess.Check(access, supplier, c.Request.CallerPersonId, "supplier.orders", ct);
        if (!allowed.IsSuccess) return Result<OrderResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(2)).DateTime);
        var decisions = (c.Request.Decisions ?? []).Select(d => new LineDecision(d.LineId, d.Action, d.Cases, d.CasePriceCents)).ToList();
        var replied = order.Respond(decisions, c.Request.DeliveryDate, today, now);
        if (!replied.IsSuccess) return Result<OrderResponse>.Fail(replied.Error!.Code, replied.Error.Message);
        await orders.Save(ct);

        // The shop hears about every reply, and exactly what is different from what they asked for.
        var summary = order.Status == OrderStatus.Cancelled
            ? "The supplier could not supply any of it, so the order is cancelled."
            : $"Confirmed for delivery on {order.DeliveryDate:yyyy-MM-dd}." + (order.HasChangesFromOrder ? "\nChanges:\n" + Changes(order) : "");
        await notifier.ToShopOwner(order.ShopId, "order.update", new Dictionary<string, string>
        {
            ["order"] = order.Reference, ["supplier"] = supplier.Name, ["summary"] = summary, ["total"] = Rands.Rand(order.TotalCents),
        }, $"order:{order.Id:N}:reply", ct);

        return Result<OrderResponse>.Ok(OrderMapping.ToResponse(order, supplier.Name));
    }

    private static string Changes(PurchaseOrder o) => string.Join("\n", o.Lines.Where(l => l.Status is LineStatus.Changed or LineStatus.Declined).Select(l =>
        l.Status == LineStatus.Declined
            ? $"- {l.Name}: not available"
            : $"- {l.Name}: {l.ConfirmedCases} cases (asked for {l.OrderedCases}) at {Rands.Rand(l.CasePriceCents)} a case"));
}

// ---------------------------------------------------------------- M4-4: deliver and receive

public sealed record DeliverOrder(Guid OrderId, DeliverOrderRequest Request) : ICommand<OrderResponse>;

public sealed record ReceiveOrder(Guid OrderId, ReceiveOrderRequest Request) : ICommand<OrderResponse>;

internal sealed class DeliveryHandlers(
    ISupplierStore suppliers, IOrderStore orders, IShopMarketplaceStore shopMarketplace, IOrganisationAccess access, TimeProvider clock)
    : ICommandHandler<DeliverOrder, OrderResponse>, ICommandHandler<ReceiveOrder, OrderResponse>
{
    public async Task<Result<OrderResponse>> Handle(DeliverOrder c, CancellationToken ct)
    {
        var order = await orders.Get(c.OrderId, ct);
        if (order is null) return Result<OrderResponse>.Fail("order.not_found", "Order not found.");
        var supplier = (await suppliers.Get(order.SupplierId, ct))!;
        var allowed = await SupplierAccess.Check(access, supplier, c.Request.CallerPersonId, "supplier.orders", ct);
        if (!allowed.IsSuccess) return Result<OrderResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var delivered = order.MarkDelivered(c.Request.DeliveredCases ?? [], clock.GetUtcNow());
        if (!delivered.IsSuccess) return Result<OrderResponse>.Fail(delivered.Error!.Code, delivered.Error.Message);
        await orders.Save(ct);
        return Result<OrderResponse>.Ok(OrderMapping.ToResponse(order, supplier.Name));
    }

    /// <summary>
    /// The shop confirms what arrived. That closes the order's delivery, remembers what was paid (so next time this supplier shows its last
    /// price), makes the supplier a usual one, and announces the delivery so ShopCore can fill in the stock-in review.
    /// </summary>
    public async Task<Result<OrderResponse>> Handle(ReceiveOrder c, CancellationToken ct)
    {
        var order = await orders.Get(c.OrderId, ct);
        if (order is null) return Result<OrderResponse>.Fail("order.not_found", "Order not found.");
        if (!await access.Has(c.Request.CallerPersonId, order.ShopId, "stock.receive", ct)) return Result<OrderResponse>.Fail("permission.denied", "You do not have access to this order.");
        var supplier = (await suppliers.Get(order.SupplierId, ct))!;

        var now = clock.GetUtcNow();
        var received = order.Receive(c.Request.ReceivedCases, now);
        if (!received.IsSuccess) return Result<OrderResponse>.Fail(received.Error!.Code, received.Error.Message);

        foreach (var line in order.Lines.Where(l => l.EffectiveCases > 0 && l.Barcode is not null))
        {
            var price = await orders.GetLastPrice(order.ShopId, order.SupplierId, line.Barcode!, ct);
            if (price is null) await orders.Add(new LastPrice(order.ShopId, order.SupplierId, line.Barcode!, line.CasePriceCents, now), ct);
            else price.Update(line.CasePriceCents, now);
        }

        var usual = await shopMarketplace.GetShopSupplier(order.ShopId, order.SupplierId, ct);
        if (usual is null) await shopMarketplace.Add(new ShopSupplier(order.ShopId, order.SupplierId), ct);
        else usual.RecordOrder(now);

        order.AnnounceReceived(supplier.Name);
        await orders.Save(ct);
        return Result<OrderResponse>.Ok(OrderMapping.ToResponse(order, supplier.Name));
    }
}

// ---------------------------------------------------------------- reading orders

public sealed record GetOrder(Guid OrderId, Guid CallerPersonId) : IQuery<OrderResponse>;

public sealed record ListShopOrders(Guid ShopId, Guid CallerPersonId) : IQuery<List<OrderResponse>>;

/// <summary>The supplier's inbox. Status is optional: draft orders are never shown to the supplier.</summary>
public sealed record ListSupplierOrders(Guid SupplierId, Guid CallerPersonId, string? Status) : IQuery<List<OrderResponse>>;

internal sealed class OrderQueries(ISupplierStore suppliers, IOrderStore orders, IOrganisationAccess access)
    : IQueryHandler<GetOrder, OrderResponse>, IQueryHandler<ListShopOrders, List<OrderResponse>>, IQueryHandler<ListSupplierOrders, List<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(GetOrder q, CancellationToken ct)
    {
        var order = await orders.Get(q.OrderId, ct);
        if (order is null) return Result<OrderResponse>.Fail("order.not_found", "Order not found.");
        var supplier = (await suppliers.Get(order.SupplierId, ct))!;

        var shopSide = await access.Has(q.CallerPersonId, order.ShopId, "stock.receive", ct);
        var supplierSide = order.Status != OrderStatus.Draft && (await SupplierAccess.Check(access, supplier, q.CallerPersonId, "supplier.orders", ct)).IsSuccess;
        return shopSide || supplierSide
            ? Result<OrderResponse>.Ok(OrderMapping.ToResponse(order, supplier.Name))
            : Result<OrderResponse>.Fail("permission.denied", "You do not have access to this order.");
    }

    public async Task<Result<List<OrderResponse>>> Handle(ListShopOrders q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.ShopId, "stock.receive", ct)) return Result<List<OrderResponse>>.Fail("permission.denied", "You do not have access to this shop.");
        var list = new List<OrderResponse>();
        foreach (var o in (await orders.ForShop(q.ShopId, ct)).OrderByDescending(o => o.CreatedAt))
            list.Add(OrderMapping.ToResponse(o, (await suppliers.Get(o.SupplierId, ct))?.Name ?? ""));
        return Result<List<OrderResponse>>.Ok(list);
    }

    public async Task<Result<List<OrderResponse>>> Handle(ListSupplierOrders q, CancellationToken ct)
    {
        var supplier = await suppliers.Get(q.SupplierId, ct);
        if (supplier is null) return Result<List<OrderResponse>>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, q.CallerPersonId, "supplier.orders", ct);
        if (!allowed.IsSuccess) return Result<List<OrderResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);

        OrderStatus? status = null;
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            if (!Enum.TryParse<OrderStatus>(q.Status, ignoreCase: true, out var parsed)) return Result<List<OrderResponse>>.Fail("order.status_invalid", "Unknown status.");
            status = parsed;
        }
        var inbox = (await orders.ForSupplier(supplier.Id, status, ct)).Where(o => o.Status != OrderStatus.Draft).OrderByDescending(o => o.SentAt);
        return Result<List<OrderResponse>>.Ok(inbox.Select(o => OrderMapping.ToResponse(o, supplier.Name)).ToList());
    }
}
