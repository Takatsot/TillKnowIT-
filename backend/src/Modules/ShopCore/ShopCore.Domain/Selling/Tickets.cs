using System.Text.Json;
using System.Text.RegularExpressions;
using TillKnowIT.SharedKernel;

namespace ShopCore.Domain.Selling;

/// <summary>One line of a parked sale. AddOnProductIds are the modifiers chosen for this unit of the item.</summary>
public sealed record TicketLine(Guid ProductId, string Name, int Quantity, long PriceCents, int UnitDivisor = 1, string? Note = null, IReadOnlyList<Guid>? AddOnProductIds = null)
{
    /// <summary>Lines that are the same item, with the same note and add-ons, are one line with the quantities added.</summary>
    public string Key => $"{ProductId}|{PriceCents}|{UnitDivisor}|{Note?.Trim().ToLowerInvariant()}|{string.Join(",", (AddOnProductIds ?? []).Order())}";

    public long TotalCents => SaleMath.LineTotal(PriceCents, Quantity, UnitDivisor);
}

public enum TicketStatus { Open, Closed, Voided }

/// <summary>
/// B2-6 and B3-1: a sale parked so it can be finished later, or a table's running order. Every till in the shop sees it after sync. One till holds a ticket
/// while it is editing it and the others see it locked; the lock lapses after 10 minutes, so a till that goes offline mid-edit cannot lock a table for ever.
/// </summary>
public sealed class OpenTicket : VersionedEntity
{
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(10);
    public const int MaxLines = 100;

    private OpenTicket() { }

    public Guid ShopId { get; private set; }
    public string Label { get; private set; } = default!;
    public Guid? TableId { get; private set; }
    public string LinesJson { get; private set; } = "[]";
    public TicketStatus Status { get; private set; }
    public Guid? HeldByDeviceId { get; private set; }
    public DateTimeOffset? HeldUntil { get; private set; }
    public Guid? ClosedSaleId { get; private set; }

    public IReadOnlyList<TicketLine> Lines => JsonSerializer.Deserialize<List<TicketLine>>(LinesJson) ?? [];
    public long TotalCents => Lines.Sum(l => l.TotalCents);

    public static Result<OpenTicket> Open(Guid id, Guid shopId, Guid staffId, Guid deviceId, string? label, Guid? tableId, IReadOnlyList<TicketLine> lines, DateTimeOffset now)
    {
        var text = label?.Trim() ?? "";
        if (text.Length is 0 or > 40) return Result<OpenTicket>.Fail("ticket.label_required", "Name or number the ticket (up to 40 characters).");
        var valid = CheckLines(lines);
        if (!valid.IsSuccess) return Result<OpenTicket>.Fail(valid.Error!.Code, valid.Error.Message);

        var ticket = new OpenTicket { ShopId = shopId, Label = text, TableId = tableId, LinesJson = Serialise(lines), Status = TicketStatus.Open, HeldByDeviceId = deviceId, HeldUntil = now + LockFor };
        ticket.StampFromDevice(id, now, staffId, deviceId);
        return Result<OpenTicket>.Ok(ticket);
    }

    /// <summary>Takes the lock for editing. Fails while another till holds it and has not let it lapse.</summary>
    public Result Hold(Guid deviceId, DateTimeOffset now)
    {
        if (Status != TicketStatus.Open) return Result.Fail("ticket.not_open", "This ticket is closed.");
        if (IsLockedAgainst(deviceId, now)) return Result.Fail("ticket.locked", "Another till is working on this ticket.");
        HeldByDeviceId = deviceId;
        HeldUntil = now + LockFor;
        Touch();
        return Result.Ok();
    }

    public Result Release(Guid deviceId)
    {
        if (HeldByDeviceId == deviceId)
        {
            HeldByDeviceId = null;
            HeldUntil = null;
            Touch();
        }
        return Result.Ok();
    }

    public bool IsLockedAgainst(Guid deviceId, DateTimeOffset now) => HeldByDeviceId is { } h && h != deviceId && HeldUntil is { } until && until > now;

    /// <summary>Saves new lines. Only the till that holds the lock may.</summary>
    public Result Edit(Guid deviceId, IReadOnlyList<TicketLine> lines, DateTimeOffset now)
    {
        var held = RequireHeld(deviceId, now);
        if (!held.IsSuccess) return held;
        var valid = CheckLines(lines);
        if (!valid.IsSuccess) return valid;
        LinesJson = Serialise(lines);
        HeldUntil = now + LockFor;
        Touch();
        return Result.Ok();
    }

    public Result MoveToTable(Guid deviceId, Guid? tableId, DateTimeOffset now)
    {
        var held = RequireHeld(deviceId, now);
        if (!held.IsSuccess) return held;
        TableId = tableId;
        Touch();
        return Result.Ok();
    }

    /// <summary>Joins another ticket's lines into this one; the other is voided. Both must be held by the same till.</summary>
    public Result MergeIn(OpenTicket other, Guid deviceId, DateTimeOffset now)
    {
        if (other.Id == Id) return Result.Fail("ticket.merge_self", "A ticket cannot be merged into itself.");
        var mine = RequireHeld(deviceId, now);
        if (!mine.IsSuccess) return mine;
        var theirs = other.RequireHeld(deviceId, now);
        if (!theirs.IsSuccess) return theirs;

        var merged = Lines.Concat(other.Lines).GroupBy(l => l.Key).Select(g => g.First() with { Quantity = g.Sum(l => l.Quantity) }).ToList();
        var valid = CheckLines(merged);
        if (!valid.IsSuccess) return valid;

        LinesJson = Serialise(merged);
        Touch();
        other.Void();
        return Result.Ok();
    }

    public Result Close(Guid saleId)
    {
        if (Status != TicketStatus.Open) return Result.Fail("ticket.not_open", "This ticket is closed.");
        Status = TicketStatus.Closed;
        ClosedSaleId = saleId;
        HeldByDeviceId = null;
        HeldUntil = null;
        Touch();
        return Result.Ok();
    }

    public void Void()
    {
        if (Status != TicketStatus.Open) return;
        Status = TicketStatus.Voided;
        HeldByDeviceId = null;
        HeldUntil = null;
        Touch();
    }

    private Result RequireHeld(Guid deviceId, DateTimeOffset now)
    {
        if (Status != TicketStatus.Open) return Result.Fail("ticket.not_open", "This ticket is closed.");
        if (HeldByDeviceId != deviceId) return Result.Fail(IsLockedAgainst(deviceId, now) ? "ticket.locked" : "ticket.not_held", IsLockedAgainst(deviceId, now) ? "Another till is working on this ticket." : "Open the ticket before changing it.");
        if (HeldUntil is { } until && until <= now) return Result.Fail("ticket.not_held", "Your hold on this ticket ran out. Open it again.");
        return Result.Ok();
    }

    private static Result CheckLines(IReadOnlyList<TicketLine> lines)
    {
        if (lines.Count > MaxLines) return Result.Fail("ticket.too_many_lines", $"A ticket has at most {MaxLines} lines.");
        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l.Name)) return Result.Fail("ticket.line_name", "Every item needs a name.");
            if (l.Quantity <= 0) return Result.Fail("ticket.line_quantity", "Item quantity must be more than zero.");
            if (l.PriceCents < 0) return Result.Fail("ticket.line_price", "Prices cannot be negative.");
            if (!SaleMath.IsValidDivisor(l.UnitDivisor)) return Result.Fail("ticket.line_unit", "The unit of an item is each or by weight.");
            if (l.Note is { Length: > 200 }) return Result.Fail("ticket.note_too_long", "A note is at most 200 characters.");
        }
        return Result.Ok();
    }

    private static string Serialise(IReadOnlyList<TicketLine> lines) => JsonSerializer.Serialize(lines);
}

public enum KitchenStatus { New, Preparing, Ready, Collected, Cancelled }

public sealed record KitchenItem(string Name, int Quantity, string? Note = null);

/// <summary>
/// B3-2: what the kitchen has to make. Order numbers are made on the till with that till's prefix so two tills offline never clash. The cook marks it
/// ready and the cashier sees it; the till shows the same list on a tablet.
/// </summary>
public sealed partial class KitchenOrder : VersionedEntity
{
    private KitchenOrder() { }

    public Guid ShopId { get; private set; }
    public string OrderNo { get; private set; } = default!;
    public Guid? TicketId { get; private set; }
    public Guid? SaleId { get; private set; }
    public string? TableLabel { get; private set; }
    public string ItemsJson { get; private set; } = "[]";
    public KitchenStatus Status { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }

    public IReadOnlyList<KitchenItem> Items => JsonSerializer.Deserialize<List<KitchenItem>>(ItemsJson) ?? [];

    public static Result<KitchenOrder> Create(Guid id, Guid shopId, Guid staffId, Guid deviceId, string? orderNo, Guid? ticketId, Guid? saleId, string? tableLabel, IReadOnlyList<KitchenItem> items, DateTimeOffset now)
    {
        var no = orderNo?.Trim().ToUpperInvariant() ?? "";
        if (!OrderNoShape().IsMatch(no)) return Result<KitchenOrder>.Fail("kitchen.order_no_invalid", "An order number is letters, digits and dashes, up to 12 characters.");
        if (items.Count is 0 or > 100) return Result<KitchenOrder>.Fail("kitchen.items_invalid", "An order needs from 1 to 100 items.");
        if (items.Any(i => string.IsNullOrWhiteSpace(i.Name) || i.Quantity < 1 || i.Quantity > 99 || i.Note is { Length: > 200 }))
            return Result<KitchenOrder>.Fail("kitchen.items_invalid", "Every item needs a name and a quantity from 1 to 99.");

        var order = new KitchenOrder { ShopId = shopId, OrderNo = no, TicketId = ticketId, SaleId = saleId, TableLabel = tableLabel?.Trim(), ItemsJson = JsonSerializer.Serialize(items), Status = KitchenStatus.New };
        order.StampFromDevice(id, now, staffId, deviceId);
        return Result<KitchenOrder>.Ok(order);
    }

    public Result Start()
    {
        if (Status != KitchenStatus.New) return Result.Fail("kitchen.not_new", "This order has already been started.");
        Status = KitchenStatus.Preparing;
        Touch();
        return Result.Ok();
    }

    public Result MarkReady(DateTimeOffset now)
    {
        if (Status is not (KitchenStatus.New or KitchenStatus.Preparing)) return Result.Fail("kitchen.not_in_progress", "This order is not being made.");
        Status = KitchenStatus.Ready;
        ReadyAt = now;
        Touch();
        return Result.Ok();
    }

    public Result Collect()
    {
        if (Status != KitchenStatus.Ready) return Result.Fail("kitchen.not_ready", "This order is not ready yet.");
        Status = KitchenStatus.Collected;
        Touch();
        return Result.Ok();
    }

    public Result Cancel()
    {
        if (Status is KitchenStatus.Collected or KitchenStatus.Cancelled) return Result.Fail("kitchen.not_cancellable", "This order can no longer be cancelled.");
        Status = KitchenStatus.Cancelled;
        Touch();
        return Result.Ok();
    }

    [GeneratedRegex(@"^[A-Z0-9-]{1,12}$")]
    private static partial Regex OrderNoShape();
}
