using Identity.Contracts;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using TillKnowIT.Dispatching;
using TillKnowIT.Reporting;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Selling;

internal static class TicketMapping
{
    public static TicketLine ToLine(TicketLineDto d) => new(d.ProductId, d.Name ?? "", d.Quantity, d.PriceCents, d.UnitDivisor, d.Note, d.AddOnProductIds);

    public static TicketLineDto ToDto(TicketLine l) => new(l.ProductId, l.Name, l.Quantity, l.PriceCents, l.UnitDivisor, l.Note, l.AddOnProductIds?.ToList());

    public static List<TicketLine> ToLines(IEnumerable<TicketLineDto>? dtos) => (dtos ?? []).Select(ToLine).ToList();

    public static TicketResponse ToResponse(OpenTicket t, Guid viewerDevice, DateTimeOffset now) => new(
        t.Id, t.Label, t.TableId, t.Status.ToString().ToLowerInvariant(), t.Lines.Select(ToDto).ToList(), t.TotalCents, t.HeldByDeviceId, t.HeldUntil, t.IsLockedAgainst(viewerDevice, now));
}

// ---------------------------------------------------------------- B2-6 and B3-1: open tickets and tables

public sealed record OpenTicketCommand(Guid ShopId, OpenTicketRequest Request) : ICommand<TicketResponse>;
public sealed record ListTickets(Guid ShopId, Guid DeviceId, Guid StaffId) : IQuery<List<TicketResponse>>;
public sealed record HoldTicket(Guid ShopId, Guid TicketId, TillCall Call) : ICommand<TicketResponse>;
public sealed record ReleaseTicket(Guid ShopId, Guid TicketId, TillCall Call) : ICommand<Unit>;
public sealed record EditTicket(Guid ShopId, Guid TicketId, EditTicketRequest Request) : ICommand<TicketResponse>;
public sealed record MoveTicket(Guid ShopId, Guid TicketId, MoveTicketRequest Request) : ICommand<TicketResponse>;
public sealed record MergeTickets(Guid ShopId, Guid TicketId, MergeTicketRequest Request) : ICommand<TicketResponse>;
public sealed record CloseTicket(Guid ShopId, Guid TicketId, CloseTicketRequest Request) : ICommand<TicketResponse>;
public sealed record VoidTicket(Guid ShopId, Guid TicketId, TillCall Call) : ICommand<Unit>;

internal sealed class TicketHandlers(ISellingStore store, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<OpenTicketCommand, TicketResponse>, IQueryHandler<ListTickets, List<TicketResponse>>, ICommandHandler<HoldTicket, TicketResponse>,
      ICommandHandler<ReleaseTicket, Unit>, ICommandHandler<EditTicket, TicketResponse>, ICommandHandler<MoveTicket, TicketResponse>,
      ICommandHandler<MergeTickets, TicketResponse>, ICommandHandler<CloseTicket, TicketResponse>, ICommandHandler<VoidTicket, Unit>
{
    private Task<Result> Allowed(Guid shop, Guid device, Guid staff, CancellationToken ct) => SellingAccess.TillAndFeature(tills, gate, shop, device, staff, "sale.create", "open_tickets", ct);

    private async Task<Result<TicketResponse>> Respond(OpenTicket t, Guid device, CancellationToken ct)
    {
        await store.Save(ct);
        return Result<TicketResponse>.Ok(TicketMapping.ToResponse(t, device, clock.GetUtcNow()));
    }

    public async Task<Result<TicketResponse>> Handle(OpenTicketCommand c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<TicketResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        // The ticket id is made on the till: opening it twice is the same ticket.
        var existing = await store.GetTicket(c.ShopId, r.Id, ct);
        if (existing is not null) return Result<TicketResponse>.Ok(TicketMapping.ToResponse(existing, r.DeviceId, clock.GetUtcNow()));

        var opened = OpenTicket.Open(r.Id, c.ShopId, r.StaffId, r.DeviceId, r.Label, r.TableId, TicketMapping.ToLines(r.Lines), clock.GetUtcNow());
        if (!opened.IsSuccess) return Result<TicketResponse>.Fail(opened.Error!.Code, opened.Error.Message);
        if (r.TableId is { } table && !(await store.Tables(c.ShopId, ct)).Any(t => t.Id == table && t.IsActive)) return Result<TicketResponse>.Fail("ticket.table_unknown", "That table is not on the floor plan.");

        store.Add(opened.Value!);
        return await Respond(opened.Value!, r.DeviceId, ct);
    }

    public async Task<Result<List<TicketResponse>>> Handle(ListTickets q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<List<TicketResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var now = clock.GetUtcNow();
        return Result<List<TicketResponse>>.Ok((await store.OpenTickets(q.ShopId, ct)).OrderBy(t => t.CreatedAt).Select(t => TicketMapping.ToResponse(t, q.DeviceId, now)).ToList());
    }

    private async Task<(OpenTicket? Ticket, Error? Error)> Load(Guid shop, Guid id, Guid device, Guid staff, CancellationToken ct)
    {
        var allowed = await Allowed(shop, device, staff, ct);
        if (!allowed.IsSuccess) return (null, allowed.Error);
        var ticket = await store.GetTicket(shop, id, ct);
        return ticket is null ? (null, new Error("ticket.not_found", "Ticket not found.")) : (ticket, null);
    }

    public async Task<Result<TicketResponse>> Handle(HoldTicket c, CancellationToken ct)
    {
        var (ticket, error) = await Load(c.ShopId, c.TicketId, c.Call.DeviceId, c.Call.StaffId, ct);
        if (error is not null) return Result<TicketResponse>.Fail(error.Code, error.Message);
        var held = ticket!.Hold(c.Call.DeviceId, clock.GetUtcNow());
        return held.IsSuccess ? await Respond(ticket, c.Call.DeviceId, ct) : Result<TicketResponse>.Fail(held.Error!.Code, held.Error.Message);
    }

    public async Task<Result<Unit>> Handle(ReleaseTicket c, CancellationToken ct)
    {
        var (ticket, error) = await Load(c.ShopId, c.TicketId, c.Call.DeviceId, c.Call.StaffId, ct);
        if (error is not null) return Result<Unit>.Fail(error.Code, error.Message);
        ticket!.Release(c.Call.DeviceId);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<TicketResponse>> Handle(EditTicket c, CancellationToken ct)
    {
        var (ticket, error) = await Load(c.ShopId, c.TicketId, c.Request.DeviceId, c.Request.StaffId, ct);
        if (error is not null) return Result<TicketResponse>.Fail(error.Code, error.Message);
        var edited = ticket!.Edit(c.Request.DeviceId, TicketMapping.ToLines(c.Request.Lines), clock.GetUtcNow());
        return edited.IsSuccess ? await Respond(ticket, c.Request.DeviceId, ct) : Result<TicketResponse>.Fail(edited.Error!.Code, edited.Error.Message);
    }

    public async Task<Result<TicketResponse>> Handle(MoveTicket c, CancellationToken ct)
    {
        var (ticket, error) = await Load(c.ShopId, c.TicketId, c.Request.DeviceId, c.Request.StaffId, ct);
        if (error is not null) return Result<TicketResponse>.Fail(error.Code, error.Message);
        if (c.Request.TableId is { } table && !(await store.Tables(c.ShopId, ct)).Any(t => t.Id == table && t.IsActive)) return Result<TicketResponse>.Fail("ticket.table_unknown", "That table is not on the floor plan.");
        var moved = ticket!.MoveToTable(c.Request.DeviceId, c.Request.TableId, clock.GetUtcNow());
        return moved.IsSuccess ? await Respond(ticket, c.Request.DeviceId, ct) : Result<TicketResponse>.Fail(moved.Error!.Code, moved.Error.Message);
    }

    public async Task<Result<TicketResponse>> Handle(MergeTickets c, CancellationToken ct)
    {
        var (ticket, error) = await Load(c.ShopId, c.TicketId, c.Request.DeviceId, c.Request.StaffId, ct);
        if (error is not null) return Result<TicketResponse>.Fail(error.Code, error.Message);
        var other = await store.GetTicket(c.ShopId, c.Request.OtherTicketId, ct);
        if (other is null) return Result<TicketResponse>.Fail("ticket.not_found", "The other ticket was not found.");
        var merged = ticket!.MergeIn(other, c.Request.DeviceId, clock.GetUtcNow());
        return merged.IsSuccess ? await Respond(ticket, c.Request.DeviceId, ct) : Result<TicketResponse>.Fail(merged.Error!.Code, merged.Error.Message);
    }

    public async Task<Result<TicketResponse>> Handle(CloseTicket c, CancellationToken ct)
    {
        var (ticket, error) = await Load(c.ShopId, c.TicketId, c.Request.DeviceId, c.Request.StaffId, ct);
        if (error is not null) return Result<TicketResponse>.Fail(error.Code, error.Message);
        if (ticket!.IsLockedAgainst(c.Request.DeviceId, clock.GetUtcNow())) return Result<TicketResponse>.Fail("ticket.locked", "Another till is working on this ticket.");
        var closed = ticket.Close(c.Request.SaleId);
        return closed.IsSuccess ? await Respond(ticket, c.Request.DeviceId, ct) : Result<TicketResponse>.Fail(closed.Error!.Code, closed.Error.Message);
    }

    public async Task<Result<Unit>> Handle(VoidTicket c, CancellationToken ct)
    {
        var (ticket, error) = await Load(c.ShopId, c.TicketId, c.Call.DeviceId, c.Call.StaffId, ct);
        if (error is not null) return Result<Unit>.Fail(error.Code, error.Message);
        if (ticket!.IsLockedAgainst(c.Call.DeviceId, clock.GetUtcNow())) return Result<Unit>.Fail("ticket.locked", "Another till is working on this ticket.");
        ticket.Void();
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}

// ---------------------------------------------------------------- B3-2: the kitchen

public sealed record CreateKitchenOrderCommand(Guid ShopId, CreateKitchenOrderRequest Request) : ICommand<KitchenOrderResponse>;
public sealed record ListKitchenOrders(Guid ShopId, Guid DeviceId, Guid StaffId, string? Statuses, DateTimeOffset? Since) : IQuery<List<KitchenOrderResponse>>;
public sealed record AdvanceKitchenOrder(Guid ShopId, Guid OrderId, AdvanceKitchenOrderRequest Request) : ICommand<KitchenOrderResponse>;

internal sealed class KitchenHandlers(ISellingStore store, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<CreateKitchenOrderCommand, KitchenOrderResponse>, IQueryHandler<ListKitchenOrders, List<KitchenOrderResponse>>, ICommandHandler<AdvanceKitchenOrder, KitchenOrderResponse>
{
    private Task<Result> Allowed(Guid shop, Guid device, Guid staff, CancellationToken ct) => SellingAccess.TillAndFeature(tills, gate, shop, device, staff, "sale.create", "kitchen", ct);

    public async Task<Result<KitchenOrderResponse>> Handle(CreateKitchenOrderCommand c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<KitchenOrderResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var same = await store.GetKitchenOrder(c.ShopId, r.Id, ct);
        if (same is not null) return Result<KitchenOrderResponse>.Ok(ToResponse(same));   // sent twice: the same order

        var created = KitchenOrder.Create(r.Id, c.ShopId, r.StaffId, r.DeviceId, r.OrderNo, r.TicketId, r.SaleId, r.TableLabel,
            (r.Items ?? []).Select(i => new KitchenItem(i.Name ?? "", i.Quantity, i.Note)).ToList(), clock.GetUtcNow());
        if (!created.IsSuccess) return Result<KitchenOrderResponse>.Fail(created.Error!.Code, created.Error.Message);
        if (await store.KitchenOrderByNo(c.ShopId, created.Value!.OrderNo, ct) is not null) return Result<KitchenOrderResponse>.Fail("kitchen.order_no_taken", "That order number is already used.");

        store.Add(created.Value);
        await store.Save(ct);
        return Result<KitchenOrderResponse>.Ok(ToResponse(created.Value));
    }

    /// <summary>The kitchen screen's list. Statuses is a comma list (default: new and preparing and ready); Since keeps it short.</summary>
    public async Task<Result<List<KitchenOrderResponse>>> Handle(ListKitchenOrders q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<List<KitchenOrderResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var statuses = new List<KitchenStatus>();
        foreach (var s in (q.Statuses ?? "new,preparing,ready").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<KitchenStatus>(s, ignoreCase: true, out var parsed)) return Result<List<KitchenOrderResponse>>.Fail("kitchen.status_invalid", "Status is new, preparing, ready, collected or cancelled.");
            statuses.Add(parsed);
        }
        var since = q.Since ?? clock.GetUtcNow().AddDays(-1);
        return Result<List<KitchenOrderResponse>>.Ok((await store.KitchenOrders(q.ShopId, statuses, since, ct)).OrderBy(o => o.CreatedAt).Select(ToResponse).ToList());
    }

    public async Task<Result<KitchenOrderResponse>> Handle(AdvanceKitchenOrder c, CancellationToken ct)
    {
        var allowed = await Allowed(c.ShopId, c.Request.DeviceId, c.Request.StaffId, ct);
        if (!allowed.IsSuccess) return Result<KitchenOrderResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var order = await store.GetKitchenOrder(c.ShopId, c.OrderId, ct);
        if (order is null) return Result<KitchenOrderResponse>.Fail("kitchen.not_found", "Order not found.");

        var done = c.Request.Action?.Trim().ToLowerInvariant() switch
        {
            "start" => order.Start(),
            "ready" => order.MarkReady(clock.GetUtcNow()),
            "collect" => order.Collect(),
            "cancel" => order.Cancel(),
            _ => Result.Fail("kitchen.action_invalid", "Action is start, ready, collect or cancel."),
        };
        if (!done.IsSuccess) return Result<KitchenOrderResponse>.Fail(done.Error!.Code, done.Error.Message);
        await store.Save(ct);
        return Result<KitchenOrderResponse>.Ok(ToResponse(order));
    }

    private static KitchenOrderResponse ToResponse(KitchenOrder o) =>
        new(o.Id, o.OrderNo, o.TableLabel, o.Status.ToString().ToLowerInvariant(), o.Items.Select(i => new KitchenItemDto(i.Name, i.Quantity, i.Note)).ToList(), o.CreatedAt, o.ReadyAt);
}

// ---------------------------------------------------------------- B6-1: quotes

public sealed record CreateQuoteCommand(Guid ShopId, CreateQuoteRequest Request) : ICommand<QuoteResponse>;
public sealed record ListQuotes(Guid ShopId, Guid DeviceId, Guid StaffId) : IQuery<List<QuoteResponse>>;
public sealed record GetQuotePdf(Guid ShopId, Guid QuoteId, Guid DeviceId, Guid StaffId) : IQuery<ReportFile>;
public sealed record ConvertQuote(Guid ShopId, Guid QuoteId, TillCall Call) : ICommand<TicketResponse>;
public sealed record CancelQuote(Guid ShopId, Guid QuoteId, TillCall Call) : ICommand<Unit>;

internal sealed class QuoteHandlers(ISellingStore store, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<CreateQuoteCommand, QuoteResponse>, IQueryHandler<ListQuotes, List<QuoteResponse>>, IQueryHandler<GetQuotePdf, ReportFile>,
      ICommandHandler<ConvertQuote, TicketResponse>, ICommandHandler<CancelQuote, Unit>
{
    private Task<Result> Allowed(Guid shop, Guid device, Guid staff, CancellationToken ct) => SellingAccess.TillAndFeature(tills, gate, shop, device, staff, "sale.create", "quotes", ct);

    public async Task<Result<QuoteResponse>> Handle(CreateQuoteCommand c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<QuoteResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var same = await store.GetQuote(c.ShopId, r.Id, ct);
        if (same is not null) return Result<QuoteResponse>.Ok(ToResponse(same));

        var created = Quote.Create(r.Id, c.ShopId, r.StaffId, r.DeviceId, r.Number, r.CustomerName, r.CustomerPhone, r.CustomerId, TicketMapping.ToLines(r.Lines), r.ExpiresOn, clock.GetUtcNow());
        if (!created.IsSuccess) return Result<QuoteResponse>.Fail(created.Error!.Code, created.Error.Message);
        store.Add(created.Value!);
        await store.Save(ct);
        return Result<QuoteResponse>.Ok(ToResponse(created.Value!));
    }

    public async Task<Result<List<QuoteResponse>>> Handle(ListQuotes q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<List<QuoteResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<List<QuoteResponse>>.Ok((await store.Quotes(q.ShopId, ct)).OrderByDescending(x => x.CreatedAt).Select(ToResponse).ToList());
    }

    /// <summary>A printable quote: the customer, every line, the total and the date it is valid to.</summary>
    public async Task<Result<ReportFile>> Handle(GetQuotePdf q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<ReportFile>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var quote = await store.GetQuote(q.ShopId, q.QuoteId, ct);
        if (quote is null) return Result<ReportFile>.Fail("quote.not_found", "Quote not found.");

        var table = new ReportTable($"Quote {quote.Number}",
            [new("Item"), new("Quantity", ColumnKind.Integer), new("Price", ColumnKind.Money), new("Total", ColumnKind.Money)],
            quote.Lines.Select(l => new object?[] { l.Name, l.Quantity, l.PriceCents, l.TotalCents }).ToList())
        {
            Notes = [
                $"For: {quote.CustomerName ?? "customer"}{(quote.CustomerPhone is null ? "" : " " + quote.CustomerPhone)}",
                $"Total: R {CellText.Rand(quote.TotalCents)}",
                $"Valid until {quote.ExpiresOn:yyyy-MM-dd}",
            ],
        };
        return Result<ReportFile>.Ok(ReportExporter.Export(table, ReportFormat.Pdf, $"quote-{quote.Number}"));
    }

    /// <summary>One tap: the quote becomes an open ticket with its lines, ready to be paid, and is marked converted.</summary>
    public async Task<Result<TicketResponse>> Handle(ConvertQuote c, CancellationToken ct)
    {
        var allowed = await Allowed(c.ShopId, c.Call.DeviceId, c.Call.StaffId, ct);
        if (!allowed.IsSuccess) return Result<TicketResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var quote = await store.GetQuote(c.ShopId, c.QuoteId, ct);
        if (quote is null) return Result<TicketResponse>.Fail("quote.not_found", "Quote not found.");

        var now = clock.GetUtcNow();
        var ticketId = Guid.CreateVersion7();
        var converted = quote.Convert(ticketId, BusinessDay.Of(now));
        if (!converted.IsSuccess) return Result<TicketResponse>.Fail(converted.Error!.Code, converted.Error.Message);

        var ticket = OpenTicket.Open(ticketId, c.ShopId, c.Call.StaffId, c.Call.DeviceId, $"Quote {quote.Number}", null, quote.Lines, now);
        if (!ticket.IsSuccess) return Result<TicketResponse>.Fail(ticket.Error!.Code, ticket.Error.Message);
        store.Add(ticket.Value!);
        await store.Save(ct);
        return Result<TicketResponse>.Ok(TicketMapping.ToResponse(ticket.Value!, c.Call.DeviceId, now));
    }

    public async Task<Result<Unit>> Handle(CancelQuote c, CancellationToken ct)
    {
        var allowed = await Allowed(c.ShopId, c.Call.DeviceId, c.Call.StaffId, ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var quote = await store.GetQuote(c.ShopId, c.QuoteId, ct);
        if (quote is null) return Result<Unit>.Fail("quote.not_found", "Quote not found.");
        var cancelled = quote.Cancel();
        if (!cancelled.IsSuccess) return Result<Unit>.Fail(cancelled.Error!.Code, cancelled.Error.Message);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    private QuoteResponse ToResponse(Quote q) => new(
        q.Id, q.Number, q.CustomerName, q.CustomerPhone, q.Lines.Select(TicketMapping.ToDto).ToList(), q.TotalCents, q.ExpiresOn,
        q.StatusOn(BusinessDay.Of(clock.GetUtcNow())).ToString().ToLowerInvariant(), q.TicketId);
}
