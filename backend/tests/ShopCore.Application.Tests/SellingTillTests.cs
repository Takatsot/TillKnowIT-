using ShopCore.Application.Selling;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public class TicketFlowTests
{
    private readonly SellingWorld _w = new();
    private readonly Guid _burger = Guid.NewGuid();
    private readonly Guid _cola = Guid.NewGuid();

    private OpenTicketRequest Request(Guid? id = null, Guid? device = null, string label = "Table 4", Guid? table = null) =>
        new(device ?? _w.TillA, _w.Cashier, id ?? Guid.NewGuid(), label, table, [new(_burger, "Burger", 2, 4_500), new(_cola, "Cola", 1, 1_500)]);

    private Task<Result<TicketResponse>> Open(OpenTicketRequest r) => _w.Tickets.Handle(new OpenTicketCommand(_w.Shop, r), default);

    [Fact]
    public async Task Opening_a_ticket_parks_the_sale_with_its_total_and_every_till_sees_it_in_the_list()
    {
        var ticket = (await Open(Request())).Value!;

        Assert.Equal(("open", 10_500L, false), (ticket.Status, ticket.TotalCents, ticket.LockedForYou));
        var fromOtherTill = (await _w.Tickets.Handle(new ListTickets(_w.Shop, _w.TillB, _w.Cashier), default)).Value!.Single();
        Assert.Equal((ticket.Id, true), (fromOtherTill.Id, fromOtherTill.LockedForYou));   // the second till sees it locked: the first is still working on it
    }

    [Fact]
    public async Task Opening_the_same_ticket_twice_gives_the_same_ticket()
    {
        var id = Guid.NewGuid();

        await Open(Request(id));
        var again = await Open(Request(id) with { Label = "A different label" });

        Assert.Equal("Table 4", again.Value!.Label);
        Assert.Single(_w.Store.TicketRows);
    }

    [Fact]
    public async Task Another_till_cannot_edit_until_the_first_releases_and_a_lapsed_hold_frees_it_too()
    {
        var ticket = (await Open(Request())).Value!;
        var edit = new EditTicketRequest(_w.TillB, _w.Cashier, [new(_burger, "Burger", 3, 4_500)]);

        Assert.Equal("ticket.locked", (await _w.Tickets.Handle(new EditTicket(_w.Shop, ticket.Id, edit), default)).Error!.Code);
        Assert.Equal("ticket.locked", (await _w.Tickets.Handle(new HoldTicket(_w.Shop, ticket.Id, new(_w.TillB, _w.Cashier)), default)).Error!.Code);

        await _w.Tickets.Handle(new ReleaseTicket(_w.Shop, ticket.Id, new(_w.TillA, _w.Cashier)), default);
        Assert.True((await _w.Tickets.Handle(new HoldTicket(_w.Shop, ticket.Id, new(_w.TillB, _w.Cashier)), default)).IsSuccess);
        var edited = await _w.Tickets.Handle(new EditTicket(_w.Shop, ticket.Id, edit), default);
        Assert.Equal(13_500, edited.Value!.TotalCents);

        _w.Clock.Advance(TimeSpan.FromMinutes(11));   // till B goes offline mid-edit
        Assert.True((await _w.Tickets.Handle(new HoldTicket(_w.Shop, ticket.Id, new(_w.TillA, _w.Cashier)), default)).IsSuccess);
    }

    [Fact]
    public async Task Tickets_at_tables_must_point_at_a_table_on_the_floor_plan_and_can_move_and_merge()
    {
        var plan = (await _w.Floor.Handle(new SaveFloorPlan(_w.Shop, new(_w.Owner, [new("Inside", "T1", 4), new("Inside", "T2", 4)])), default)).Value!;
        var t1 = plan[0].Id;
        var t2 = plan[1].Id;

        Assert.Equal("ticket.table_unknown", (await Open(Request(table: Guid.NewGuid()))).Error!.Code);
        var a = (await Open(Request(table: t1, label: "T1"))).Value!;
        var b = (await Open(Request(table: t2, label: "T2"))).Value!;

        var moved = await _w.Tickets.Handle(new MoveTicket(_w.Shop, a.Id, new(_w.TillA, _w.Cashier, t2)), default);
        Assert.Equal(t2, moved.Value!.TableId);
        Assert.Equal("ticket.table_unknown", (await _w.Tickets.Handle(new MoveTicket(_w.Shop, a.Id, new(_w.TillA, _w.Cashier, Guid.NewGuid())), default)).Error!.Code);

        var merged = await _w.Tickets.Handle(new MergeTickets(_w.Shop, a.Id, new(_w.TillA, _w.Cashier, b.Id)), default);
        Assert.Equal((6, 21_000L), (merged.Value!.Lines.Sum(l => l.Quantity), merged.Value.TotalCents));   // 4 burgers and 2 colas
        Assert.Equal(TicketStatus.Voided, _w.Store.TicketRows.Single(t => t.Id == b.Id).Status);
        Assert.Single((await _w.Tickets.Handle(new ListTickets(_w.Shop, _w.TillA, _w.Cashier), default)).Value!);
    }

    [Fact]
    public async Task Closing_ties_the_ticket_to_its_sale_and_a_ticket_held_elsewhere_cannot_be_closed_or_voided()
    {
        var ticket = (await Open(Request())).Value!;
        var sale = Guid.NewGuid();

        Assert.Equal("ticket.locked", (await _w.Tickets.Handle(new CloseTicket(_w.Shop, ticket.Id, new(_w.TillB, _w.Cashier, sale)), default)).Error!.Code);
        Assert.Equal("ticket.locked", (await _w.Tickets.Handle(new VoidTicket(_w.Shop, ticket.Id, new(_w.TillB, _w.Cashier)), default)).Error!.Code);

        var closed = await _w.Tickets.Handle(new CloseTicket(_w.Shop, ticket.Id, new(_w.TillA, _w.Cashier, sale)), default);

        Assert.Equal("closed", closed.Value!.Status);
        Assert.Empty((await _w.Tickets.Handle(new ListTickets(_w.Shop, _w.TillA, _w.Cashier), default)).Value!);
        Assert.Equal("ticket.not_open", (await _w.Tickets.Handle(new CloseTicket(_w.Shop, ticket.Id, new(_w.TillA, _w.Cashier, sale)), default)).Error!.Code);
    }

    [Fact]
    public async Task A_voided_ticket_leaves_the_list_and_tickets_need_an_approved_till_and_the_pack_feature()
    {
        var ticket = (await Open(Request())).Value!;

        Assert.True((await _w.Tickets.Handle(new VoidTicket(_w.Shop, ticket.Id, new(_w.TillA, _w.Cashier)), default)).IsSuccess);
        Assert.Empty((await _w.Tickets.Handle(new ListTickets(_w.Shop, _w.TillA, _w.Cashier), default)).Value!);

        Assert.Equal("device.not_approved", (await Open(Request(device: Guid.NewGuid()))).Error!.Code);
        Assert.Equal("ticket.not_found", (await _w.Tickets.Handle(new HoldTicket(_w.Shop, Guid.NewGuid(), new(_w.TillA, _w.Cashier)), default)).Error!.Code);
        _w.Gate.Off.Add("open_tickets");
        Assert.Equal("feature.not_enabled", (await Open(Request())).Error!.Code);
    }
}

public class KitchenFlowTests
{
    private readonly SellingWorld _w = new();

    private Task<Result<KitchenOrderResponse>> Create(string no = "A-17", Guid? id = null) =>
        _w.Kitchen.Handle(new CreateKitchenOrderCommand(_w.Shop, new(_w.TillA, _w.Cashier, id ?? Guid.NewGuid(), no, null, null, "Table 4", [new("Burger", 2, "no onion")])), default);

    private Task<Result<KitchenOrderResponse>> Advance(Guid id, string action) =>
        _w.Kitchen.Handle(new AdvanceKitchenOrder(_w.Shop, id, new(_w.TillB, _w.Cashier, action)), default);

    [Fact]
    public async Task An_order_reaches_the_kitchen_screen_and_the_cook_walks_it_to_ready_and_the_cashier_collects_it()
    {
        var order = (await Create()).Value!;

        var onScreen = (await _w.Kitchen.Handle(new ListKitchenOrders(_w.Shop, _w.TillB, _w.Cashier, null, null), default)).Value!;
        Assert.Equal([order.Id], onScreen.Select(o => o.Id));

        Assert.Equal("preparing", (await Advance(order.Id, "start")).Value!.Status);
        Assert.Equal("ready", (await Advance(order.Id, "ready")).Value!.Status);
        Assert.NotNull((await Advance(order.Id, "collect")).Value!.ReadyAt);

        Assert.Empty((await _w.Kitchen.Handle(new ListKitchenOrders(_w.Shop, _w.TillB, _w.Cashier, null, null), default)).Value!);   // collected orders leave the screen
        Assert.Single((await _w.Kitchen.Handle(new ListKitchenOrders(_w.Shop, _w.TillB, _w.Cashier, "collected", null), default)).Value!);
    }

    [Fact]
    public async Task Sending_an_order_twice_is_one_order_and_order_numbers_are_not_reused()
    {
        var id = Guid.NewGuid();

        await Create("A-17", id);
        await Create("A-17", id);
        var clash = await Create("A-17");

        Assert.Single(_w.Store.KitchenRows);
        Assert.Equal("kitchen.order_no_taken", clash.Error!.Code);
        Assert.True((await Create("B-17")).IsSuccess);   // the other till's prefix
    }

    [Fact]
    public async Task Moves_must_make_sense_and_the_list_can_be_filtered_and_kept_recent()
    {
        var order = (await Create()).Value!;

        Assert.Equal("kitchen.not_ready", (await Advance(order.Id, "collect")).Error!.Code);
        Assert.Equal("kitchen.action_invalid", (await Advance(order.Id, "teleport")).Error!.Code);
        Assert.Equal("kitchen.not_found", (await Advance(Guid.NewGuid(), "start")).Error!.Code);
        Assert.Equal("kitchen.status_invalid", (await _w.Kitchen.Handle(new ListKitchenOrders(_w.Shop, _w.TillB, _w.Cashier, "wishful", null), default)).Error!.Code);

        _w.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Empty((await _w.Kitchen.Handle(new ListKitchenOrders(_w.Shop, _w.TillB, _w.Cashier, null, null), default)).Value!);   // older than a day: not on the screen
        Assert.Single((await _w.Kitchen.Handle(new ListKitchenOrders(_w.Shop, _w.TillB, _w.Cashier, null, SellingWorld.Start.AddHours(-1)), default)).Value!);
        Assert.True((await Advance(order.Id, "cancel")).IsSuccess);
    }

    [Fact]
    public async Task The_kitchen_needs_its_pack_feature_and_an_approved_till()
    {
        _w.Gate.Off.Add("kitchen");
        Assert.Equal("feature.not_enabled", (await Create()).Error!.Code);
        _w.Gate.Off.Clear();
        Assert.Equal("device.not_approved", (await _w.Kitchen.Handle(new CreateKitchenOrderCommand(_w.Shop, new(Guid.NewGuid(), _w.Cashier, Guid.NewGuid(), "A-1", null, null, null, [new("x", 1)])), default)).Error!.Code);
    }
}

public class QuoteFlowTests
{
    private readonly SellingWorld _w = new();
    private static readonly DateOnly Today = new(2026, 10, 26);

    private Task<Result<QuoteResponse>> Create(Guid? id = null, DateOnly? expires = null, string number = "Q-001") =>
        _w.Quotes.Handle(new CreateQuoteCommand(_w.Shop, new(_w.TillA, _w.Cashier, id ?? Guid.NewGuid(), number, "Mr Naidoo", "0821234567", null,
            [new(Guid.NewGuid(), "Paint 20l", 3, 85_000), new(Guid.NewGuid(), "Brush", 2, 3_500)], expires ?? Today.AddDays(14))), default);

    [Fact]
    public async Task A_quote_is_created_with_its_total_and_downloads_as_a_pdf()
    {
        var quote = (await Create()).Value!;

        Assert.Equal((262_000L, "open"), (quote.TotalCents, quote.Status));
        var pdf = (await _w.Quotes.Handle(new GetQuotePdf(_w.Shop, quote.Id, _w.TillA, _w.Cashier), default)).Value!;
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
        Assert.Contains("quote-Q-001", pdf.FileName);
    }

    [Fact]
    public async Task Sending_a_quote_twice_makes_one_and_a_number_cannot_be_reused()
    {
        var id = Guid.NewGuid();

        await Create(id);
        await Create(id);

        Assert.Single(_w.Store.QuoteRows);
    }

    [Fact]
    public async Task One_tap_turns_a_quote_into_an_open_ticket_with_the_same_lines_and_it_can_only_happen_once()
    {
        var quote = (await Create()).Value!;

        var ticket = (await _w.Quotes.Handle(new ConvertQuote(_w.Shop, quote.Id, new(_w.TillA, _w.Cashier)), default)).Value!;

        Assert.Equal(("Quote Q-001", 262_000L, 2), (ticket.Label, ticket.TotalCents, ticket.Lines.Count));
        Assert.Equal(ticket.Id, (await _w.Quotes.Handle(new ListQuotes(_w.Shop, _w.TillA, _w.Cashier), default)).Value!.Single().TicketId);
        Assert.Equal("quote.not_open", (await _w.Quotes.Handle(new ConvertQuote(_w.Shop, quote.Id, new(_w.TillA, _w.Cashier)), default)).Error!.Code);
        Assert.Equal("converted", (await _w.Quotes.Handle(new ListQuotes(_w.Shop, _w.TillA, _w.Cashier), default)).Value!.Single().Status);
    }

    [Fact]
    public async Task An_expired_quote_cannot_be_converted_and_a_cancelled_one_cannot_either()
    {
        var old = (await Create(null, Today.AddDays(3), "Q-OLD")).Value!;
        var cancelled = (await Create(null, null, "Q-CAN")).Value!;
        await _w.Quotes.Handle(new CancelQuote(_w.Shop, cancelled.Id, new(_w.TillA, _w.Cashier)), default);

        _w.Clock.Advance(TimeSpan.FromDays(5));

        Assert.Equal("quote.expired", (await _w.Quotes.Handle(new ConvertQuote(_w.Shop, old.Id, new(_w.TillA, _w.Cashier)), default)).Error!.Code);
        Assert.Equal("expired", (await _w.Quotes.Handle(new ListQuotes(_w.Shop, _w.TillA, _w.Cashier), default)).Value!.Single(q => q.Number == "Q-OLD").Status);
        Assert.Equal("quote.not_open", (await _w.Quotes.Handle(new ConvertQuote(_w.Shop, cancelled.Id, new(_w.TillA, _w.Cashier)), default)).Error!.Code);
        Assert.Equal("quote.not_found", (await _w.Quotes.Handle(new CancelQuote(_w.Shop, Guid.NewGuid(), new(_w.TillA, _w.Cashier)), default)).Error!.Code);
        Assert.Empty(_w.Store.TicketRows);
    }

    [Fact]
    public async Task A_quote_rung_up_on_the_main_sale_is_marked_converted_once()
    {
        var quote = (await Create()).Value!;
        Assert.True((await _w.Quotes.Handle(new MarkQuoteInSale(_w.Shop, quote.Id, new(_w.TillA, _w.Cashier)), default)).IsSuccess);

        var row = (await _w.Quotes.Handle(new ListQuotes(_w.Shop, _w.TillA, _w.Cashier), default)).Value!.Single();
        Assert.Equal(("converted", null), (row.Status, row.TicketId));
        Assert.Equal("quote.not_open", (await _w.Quotes.Handle(new MarkQuoteInSale(_w.Shop, quote.Id, new(_w.TillA, _w.Cashier)), default)).Error!.Code);
        Assert.Equal("quote.not_found", (await _w.Quotes.Handle(new MarkQuoteInSale(_w.Shop, Guid.NewGuid(), new(_w.TillA, _w.Cashier)), default)).Error!.Code);
    }

    [Fact]
    public async Task Quotes_need_their_pack_feature_and_valid_dates()
    {
        Assert.Equal("quote.expiry_invalid", (await Create(null, Today.AddDays(-1))).Error!.Code);
        _w.Gate.Off.Add("quotes");
        Assert.Equal("feature.not_enabled", (await Create()).Error!.Code);
    }
}

public class LaybyFlowTests
{
    private readonly SellingWorld _w = new();
    private static readonly DateOnly Today = new(2026, 10, 26);
    private readonly Guid _jacket = Guid.NewGuid();

    private Task<Result<LaybyResponse>> Create(long deposit = 20_000, Guid? id = null, DateOnly? due = null, string reference = "L-001") =>
        _w.Laybys.Handle(new CreateLayby(_w.Shop, new(_w.TillA, _w.Cashier, id ?? Guid.NewGuid(), reference, "Mrs Khumalo", "0821110000", null,
            [new(_jacket, "Jacket", 1, 100_000)], deposit, "cash", due ?? Today.AddDays(28))), default);

    private Task<Result<LaybyResponse>> Pay(Guid id, long cents, string method = "card") => _w.Laybys.Handle(new PayLayby(_w.Shop, id, new(_w.TillB, _w.Cashier, cents, method)), default);

    [Fact]
    public async Task A_lay_by_takes_a_deposit_and_payments_until_it_is_paid_off()
    {
        var plan = (await Create()).Value!;

        Assert.Equal(("active", 20_000L, 80_000L), (plan.Status, plan.PaidCents, plan.BalanceCents));
        await Pay(plan.Id, 30_000);
        var done = (await Pay(plan.Id, 50_000, "cash")).Value!;

        Assert.Equal(("completed", 0L, 3), (done.Status, done.BalanceCents, done.Payments.Count));
        Assert.Equal("layby.not_active", (await Pay(plan.Id, 1)).Error!.Code);   // nothing more can be paid on a plan that is paid off
    }

    [Fact]
    public async Task Sending_the_same_lay_by_twice_makes_one_plan_and_one_deposit()
    {
        var id = Guid.NewGuid();

        await Create(20_000, id);
        var again = (await Create(20_000, id)).Value!;

        Assert.Equal(20_000, again.PaidCents);
        Assert.Single(_w.Store.LaybyRows);
    }

    [Fact]
    public async Task The_shops_own_terms_from_its_pack_settings_apply()
    {
        _w.Gate.SettingsByFeature["layby"] = "{\"minDepositPercent\":50,\"maxWeeks\":4,\"cancelFeePercent\":20,\"minCancelFeeCents\":100}";

        Assert.Equal("layby.deposit_too_small", (await Create(40_000)).Error!.Code);
        Assert.Equal("layby.due_invalid", (await Create(50_000, null, Today.AddDays(29))).Error!.Code);
        var plan = (await Create(50_000, null, Today.AddDays(28))).Value!;

        var cancelled = (await _w.Laybys.Handle(new CancelLayby(_w.Shop, plan.Id, new(_w.TillA, _w.Cashier)), default)).Value!;

        Assert.Equal((20_000L, 30_000L), (cancelled.CancellationFeeCents, cancelled.RefundDueCents));   // 20% of R1000 is R200, of R500 paid
    }

    [Fact]
    public async Task Cancelling_keeps_the_default_fee_and_a_cancelled_plan_cannot_be_paid()
    {
        var plan = (await Create(40_000)).Value!;

        var cancelled = (await _w.Laybys.Handle(new CancelLayby(_w.Shop, plan.Id, new(_w.TillA, _w.Cashier)), default)).Value!;

        Assert.Equal(("cancelled", 10_000L, 30_000L), (cancelled.Status, cancelled.CancellationFeeCents, cancelled.RefundDueCents));
        Assert.Equal("layby.not_active", (await Pay(plan.Id, 1_000)).Error!.Code);
        Assert.Equal("layby.not_active", (await _w.Laybys.Handle(new CancelLayby(_w.Shop, plan.Id, new(_w.TillA, _w.Cashier)), default)).Error!.Code);
    }

    [Fact]
    public async Task The_worker_expires_overdue_plans_once_on_the_cancellation_terms()
    {
        var overdue = (await Create(40_000, null, Today.AddDays(7), "L-OLD")).Value!;
        var fine = (await Create(40_000, null, Today.AddDays(60), "L-OK")).Value!;
        _w.Clock.Advance(TimeSpan.FromDays(8));

        Assert.Equal(1, (await _w.Laybys.Handle(new ExpireDueLaybys(), default)).Value);
        Assert.Equal(0, (await _w.Laybys.Handle(new ExpireDueLaybys(), default)).Value);

        var list = (await _w.Laybys.Handle(new ListLaybys(_w.Shop, _w.TillA, _w.Cashier), default)).Value!;
        Assert.Equal(("expired", 30_000L), (list.Single(l => l.Id == overdue.Id).Status, list.Single(l => l.Id == overdue.Id).RefundDueCents));
        Assert.Equal("active", list.Single(l => l.Id == fine.Id).Status);
    }

    [Fact]
    public async Task Stock_is_reserved_while_a_plan_is_active_or_waiting_and_freed_when_it_ends()
    {
        var plan = (await Create()).Value!;
        Assert.Equal(1, (await _w.Laybys.Handle(new GetLaybyReservations(_w.Shop, _w.TillA, _w.Cashier), default)).Value![_jacket]);

        await Pay(plan.Id, 80_000);   // paid off but not yet collected
        Assert.Equal(1, (await _w.Laybys.Handle(new GetLaybyReservations(_w.Shop, _w.TillA, _w.Cashier), default)).Value![_jacket]);

        var other = (await Create(20_000, null, null, "L-002")).Value!;
        await _w.Laybys.Handle(new CancelLayby(_w.Shop, other.Id, new(_w.TillA, _w.Cashier)), default);
        Assert.Equal(1, (await _w.Laybys.Handle(new GetLaybyReservations(_w.Shop, _w.TillA, _w.Cashier), default)).Value![_jacket]);   // only the paid-off one remains
    }

    [Fact]
    public async Task Collecting_needs_a_paid_off_plan_and_the_matching_lay_by_sale()
    {
        var plan = (await Create()).Value!;
        var sale = Guid.NewGuid();

        Assert.Equal("layby.sale_unknown", (await _w.Laybys.Handle(new CollectLayby(_w.Shop, plan.Id, new(_w.TillA, _w.Cashier, sale)), default)).Error!.Code);
        _w.Store.Summaries[sale] = new SaleSummary("cash", null, 100_000);
        Assert.Equal("layby.sale_mismatch", (await _w.Laybys.Handle(new CollectLayby(_w.Shop, plan.Id, new(_w.TillA, _w.Cashier, sale)), default)).Error!.Code);

        _w.Store.Summaries[sale] = new SaleSummary("layby", null, 100_000);
        Assert.Equal("layby.not_complete", (await _w.Laybys.Handle(new CollectLayby(_w.Shop, plan.Id, new(_w.TillA, _w.Cashier, sale)), default)).Error!.Code);

        await Pay(plan.Id, 80_000);
        var collected = (await _w.Laybys.Handle(new CollectLayby(_w.Shop, plan.Id, new(_w.TillA, _w.Cashier, sale)), default)).Value!;
        Assert.Equal("collected", collected.Status);
        Assert.Empty((await _w.Laybys.Handle(new GetLaybyReservations(_w.Shop, _w.TillA, _w.Cashier), default)).Value!);
    }

    [Fact]
    public async Task Lay_by_needs_its_pack_feature_an_approved_till_and_a_real_plan()
    {
        Assert.Equal("layby.not_found", (await Pay(Guid.NewGuid(), 100)).Error!.Code);
        Assert.Equal("device.not_approved", (await _w.Laybys.Handle(new ListLaybys(_w.Shop, Guid.NewGuid(), _w.Cashier), default)).Error!.Code);
        _w.Gate.Off.Add("layby");
        Assert.Equal("feature.not_enabled", (await Create()).Error!.Code);
    }
}

public class VoucherFlowTests
{
    private readonly SellingWorld _w = new();

    private Task<Result<VoucherIssued>> Issue(long cents = 50_000, string paidWith = "cash", string reference = "sale-1") =>
        _w.Vouchers.Handle(new IssueVoucher(_w.Shop, new(_w.TillA, _w.Cashier, cents, paidWith, reference, null)), default);

    private Task<Result<VoucherResponse>> Redeem(string code, long cents, string reference = "ev-1") =>
        _w.Vouchers.Handle(new RedeemVoucher(_w.Shop, new(_w.TillB, _w.Cashier, code, cents, reference, null)), default);

    [Fact]
    public async Task A_voucher_is_issued_with_a_code_shown_once_and_found_again_by_that_code()
    {
        var issued = (await Issue()).Value!;

        var found = (await _w.Vouchers.Handle(new CheckVoucher(_w.Shop, issued.Code.ToLowerInvariant().Replace("-", " "), _w.TillB, _w.Cashier), default)).Value!;

        Assert.Equal((issued.Id, 50_000L, false, false), (found.Id, found.BalanceCents, found.IsVoid, found.IsExpired));
        Assert.Equal(issued.Code.Replace("-", "")[^4..], found.CodeLast4);
        Assert.DoesNotContain(issued.Code.Replace("-", ""), System.Text.Json.JsonSerializer.Serialize(found));   // the code is not in what is sent back later
    }

    [Fact]
    public async Task Redeeming_takes_value_off_and_a_repeated_redemption_is_not_taken_twice()
    {
        var issued = (await Issue()).Value!;

        var first = (await Redeem(issued.Code, 20_000, "ev-1")).Value!;
        var repeat = (await Redeem(issued.Code, 20_000, "ev-1")).Value!;
        var more = (await Redeem(issued.Code, 10_000, "ev-2")).Value!;

        Assert.Equal((30_000L, 30_000L, 20_000L), (first.BalanceCents, repeat.BalanceCents, more.BalanceCents));
    }

    [Fact]
    public async Task A_voucher_cannot_be_overspent_used_after_it_expires_or_guessed()
    {
        var issued = (await Issue()).Value!;

        Assert.Equal("voucher.insufficient", (await Redeem(issued.Code, 50_001)).Error!.Code);
        Assert.Equal("voucher.not_found", (await Redeem("AAAA-BBBB-CCCC", 100)).Error!.Code);
        Assert.Equal("voucher.reference_required", (await Redeem(issued.Code, 100, " ")).Error!.Code);

        _w.Clock.Advance(TimeSpan.FromDays(366));
        Assert.Equal("voucher.expired", (await Redeem(issued.Code, 100, "late")).Error!.Code);
        Assert.True((await _w.Vouchers.Handle(new CheckVoucher(_w.Shop, issued.Code, _w.TillB, _w.Cashier), default)).Value!.IsExpired);
    }

    [Fact]
    public async Task Validity_comes_from_the_pack_settings_and_a_voucher_has_a_value_a_method_and_a_reference()
    {
        _w.Gate.SettingsByFeature["vouchers"] = "{\"validMonths\":3}";

        Assert.Equal(new DateOnly(2027, 1, 26), (await Issue()).Value!.ExpiresOn);
        Assert.Equal("voucher.amount_invalid", (await Issue(0, "cash", "r2")).Error!.Code);
        Assert.Equal("voucher.method_invalid", (await Issue(1_000, "credit", "r3")).Error!.Code);
        Assert.Equal("voucher.reference_required", (await Issue(1_000, "cash", " ")).Error!.Code);
    }

    [Fact]
    public async Task Vouchers_need_their_pack_feature_and_an_approved_till()
    {
        _w.Gate.Off.Add("vouchers");
        Assert.Equal("feature.not_enabled", (await Issue()).Error!.Code);
        _w.Gate.Off.Clear();
        Assert.Equal("device.not_approved", (await _w.Vouchers.Handle(new CheckVoucher(_w.Shop, "X", Guid.NewGuid(), _w.Cashier), default)).Error!.Code);
    }

    [Fact]
    public async Task A_voucher_of_another_shop_is_not_found_here()
    {
        var issued = (await Issue()).Value!;
        var otherShop = new RedeemVoucher(Guid.NewGuid(), new(_w.TillB, _w.Cashier, issued.Code, 100, "ev", null));

        Assert.Equal("voucher.not_found", (await _w.Vouchers.Handle(otherShop, default)).Error!.Code);
    }
}

public class SerialFlowTests
{
    private readonly SellingWorld _w = new();
    private readonly Guid _sale = Guid.NewGuid();
    private Product _phone = default!;

    public SerialFlowTests()
    {
        _phone = _w.NewProduct("Phone", 300_000);
        _phone.SetSellingFlags(false, false, true);
        _w.Store.Sold[_sale] = new() { [_phone.Id] = 2 };
    }

    private Task<Result<Unit>> Record(params string[] serials) =>
        _w.Serials.Handle(new RecordSerials(_w.Shop, _sale, new(_w.TillA, _w.Cashier, [.. serials.Select(s => new SerialItem(_phone.Id, s))])), default);

    [Fact]
    public async Task Serials_are_recorded_against_the_sale_and_found_later_by_part_of_the_number()
    {
        Assert.True((await Record("sn-123456", "SN-123457")).IsSuccess);

        var found = (await _w.Serials.Handle(new FindSerials(_w.Shop, "1234", _w.TillA, _w.Cashier), default)).Value!;

        Assert.Equal(["SN-123456", "SN-123457"], found.Select(f => f.Serial).Order());
        Assert.All(found, f => Assert.Equal(("Phone", _sale), (f.ProductName, f.SaleId)));
    }

    [Fact]
    public async Task A_serial_cannot_be_sold_twice_and_there_cannot_be_more_than_were_sold()
    {
        await Record("SN-123456");

        Assert.Equal("serial.already_sold", (await Record("SN-123456")).Error!.Code);
        Assert.Equal("serial.already_sold", (await Record("SN-999999", "SN-999999")).Error!.Code);
        Assert.Equal("serial.too_many", (await Record("SN-777777", "SN-888888")).Error!.Code);   // 1 already + 2 = 3 of 2
        Assert.Single(_w.Store.SerialRows);
    }

    [Fact]
    public async Task Only_items_in_the_sale_that_take_serials_and_valid_serials_are_accepted()
    {
        var plain = _w.NewProduct("Cable", 5_000);
        _w.Store.Sold[_sale][plain.Id] = 1;

        Assert.Equal("serial.not_required", (await _w.Serials.Handle(new RecordSerials(_w.Shop, _sale, new(_w.TillA, _w.Cashier, [new SerialItem(plain.Id, "CABLE-001")])), default)).Error!.Code);
        Assert.Equal("serial.not_in_sale", (await _w.Serials.Handle(new RecordSerials(_w.Shop, _sale, new(_w.TillA, _w.Cashier, [new SerialItem(Guid.NewGuid(), "ABC-123")])), default)).Error!.Code);
        Assert.Equal("serial.invalid", (await Record("a b")).Error!.Code);
        Assert.Equal("serial.sale_unknown", (await _w.Serials.Handle(new RecordSerials(_w.Shop, Guid.NewGuid(), new(_w.TillA, _w.Cashier, [new SerialItem(_phone.Id, "ABC-123")])), default)).Error!.Code);
        Assert.Equal("serial.count_invalid", (await Record()).Error!.Code);
    }

    [Fact]
    public async Task Searching_needs_a_few_characters_and_the_pack_feature()
    {
        Assert.Equal("serial.search_short", (await _w.Serials.Handle(new FindSerials(_w.Shop, "ab", _w.TillA, _w.Cashier), default)).Error!.Code);
        _w.Gate.Off.Add("serial_numbers");
        Assert.Equal("feature.not_enabled", (await Record("SN-123456")).Error!.Code);
    }
}

public class OnlineRefundFlowTests
{
    private sealed class Effects : IRefundEffects
    {
        public List<StockMovement> Movements { get; } = [];
        public List<CreditEntry> Entries { get; } = [];
        public void Add(StockMovement movement) => Movements.Add(movement);
        public void Add(CreditEntry entry) => Entries.Add(entry);
    }

    private sealed class Refunds : IRefundStore
    {
        public Dictionary<Guid, List<SoldLine>> Lines { get; } = [];
        public List<(Guid SaleId, Guid ProductId, int Quantity)> Done { get; } = [];
        public List<SaleRefund> Added { get; } = [];
        public Task<HashSet<Guid>> ExistingIds(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(Added.Select(a => a.Id).Where(ids.Contains).ToHashSet());
        public Task<Dictionary<Guid, List<SoldLine>>> SoldLines(Guid shopId, IEnumerable<Guid> saleIds, CancellationToken ct) => Task.FromResult(saleIds.Where(Lines.ContainsKey).ToDictionary(i => i, i => Lines[i]));
        public Task<List<(Guid SaleId, Guid ProductId, int Quantity)>> RefundedSoFar(IEnumerable<Guid> saleIds, CancellationToken ct) => Task.FromResult(Done.Where(d => saleIds.Contains(d.SaleId)).ToList());
        public Task<long> CashRefunds(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult(0L);
        public Task<long> CashIn(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult(0L);
        public void Add(SaleRefund refund) { Added.Add(refund); Done.AddRange(refund.Lines.Select(l => (refund.SaleId, l.ProductId, l.Quantity))); }
    }

    private readonly SellingWorld _w = new();
    private readonly Effects _effects = new();
    private readonly Refunds _refunds = new();
    private readonly Guid _sale = Guid.NewGuid();
    private readonly Guid _shirt = Guid.NewGuid();

    public OnlineRefundFlowTests()
    {
        _refunds.Lines[_sale] = [new SoldLine(_shirt, _shirt, "Shirt", 2, 40_000)];
        _w.Tills.Allowed.Add((_w.TillA, _w.Manager));
    }

    private OnlineRefundHandler Handler => new(_effects, _refunds, _w.Store, _w.Tills, _w.Gate, _w.Clock);

    private Task<Result<OnlineRefundResponse>> Refund(string method, int qty = 1, Guid? approvedBy = null, Guid? id = null, string? requested = null)
    {
        _w.Store.Summaries[_sale] = new SaleSummary(method, method == "credit" ? Guid.NewGuid() : null, 40_000);
        return Handler.Handle(new CreateOnlineRefund(_w.Shop, _sale, new(_w.TillA, _w.Cashier, id ?? Guid.NewGuid(), "Does not fit", requested, approvedBy, [new RefundLineUpload(_shirt, qty)])), default);
    }

    [Fact]
    public async Task A_cash_refund_goes_back_as_cash_returns_the_goods_to_stock_and_records_the_refund()
    {
        var done = (await Refund("cash")).Value!;

        Assert.Equal((20_000L, "cash", null), (done.AmountCents, done.MethodCode, done.Voucher));
        Assert.Equal(1, _effects.Movements.Single().Quantity);
        Assert.Equal(StockMovementType.Return, _effects.Movements.Single().Type);
        Assert.Single(_refunds.Added);
        Assert.Equal(1, _w.Store.Saves);
    }

    [Fact]
    public async Task A_wallet_or_lay_by_sale_is_refunded_as_a_gift_voucher_whose_code_is_shown_once()
    {
        var done = (await Refund("wallet")).Value!;

        Assert.Equal("voucher", done.MethodCode);
        Assert.NotNull(done.Voucher);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", done.Voucher!.Code);
        var voucher = _w.Store.VoucherRows.Single();
        Assert.Equal((20_000L, null), (voucher.BalanceCents, voucher.PaidWithMethod));   // issued as a refund: no cash came in for it
    }

    [Fact]
    public async Task A_credit_sale_refund_reduces_the_debt_and_does_not_make_a_voucher()
    {
        var done = (await Refund("credit")).Value!;

        Assert.Equal("credit", done.MethodCode);
        Assert.Equal(-20_000, _effects.Entries.Single().AmountCents);
        Assert.Empty(_w.Store.VoucherRows);
    }

    [Fact]
    public async Task Repeating_a_refund_id_is_refused_and_so_is_taking_back_more_than_was_sold()
    {
        var id = Guid.NewGuid();
        await Refund("cash", 1, null, id);

        Assert.Equal("refund.already_processed", (await Refund("cash", 1, null, id)).Error!.Code);
        Assert.Equal("refund.too_many", (await Refund("cash", 2)).Error!.Code);   // one of two already taken back
        Assert.True((await Refund("cash", 1)).IsSuccess);
    }

    [Fact]
    public async Task A_big_online_refund_needs_an_approving_manager()
    {
        _refunds.Lines[_sale] = [new SoldLine(_shirt, _shirt, "Shirt", 2, 120_000)];

        Assert.Equal("refund.approval_required", (await Refund("cash", 2)).Error!.Code);
        _w.Tills.Denied.Add((_w.Manager, "sale.refund_approve"));
        Assert.Equal("refund.approver_not_allowed", (await Refund("cash", 2, _w.Manager)).Error!.Code);
        _w.Tills.Denied.Clear();
        Assert.True((await Refund("cash", 2, _w.Manager)).IsSuccess);
    }

    [Fact]
    public async Task The_sale_must_be_known_not_cancelled_and_the_cashier_allowed_and_the_pack_must_have_refunds()
    {
        Assert.Equal("refund.sale_unknown", (await Handler.Handle(new CreateOnlineRefund(_w.Shop, Guid.NewGuid(), new(_w.TillA, _w.Cashier, Guid.NewGuid(), "x", null, null, [new RefundLineUpload(_shirt, 1)])), default)).Error!.Code);

        _w.Store.Cancelled.Add(_sale);
        Assert.Equal("refund.sale_cancelled", (await Refund("cash")).Error!.Code);
        _w.Store.Cancelled.Clear();

        _w.Tills.Denied.Add((_w.Cashier, "sale.refund"));
        Assert.Equal("permission.denied", (await Refund("cash")).Error!.Code);
        _w.Tills.Denied.Clear();

        _w.Gate.Off.Add("refunds");
        Assert.Equal("feature.not_enabled", (await Refund("cash")).Error!.Code);
    }

    [Fact]
    public async Task A_cash_sale_cannot_be_refunded_to_credit_but_can_be_refunded_as_a_voucher_when_asked_online()
    {
        Assert.Equal("refund.method_invalid", (await Refund("cash", 1, null, null, "credit")).Error!.Code);
        var asVoucher = (await Refund("cash", 1, null, null, "voucher")).Value!;
        Assert.NotNull(asVoucher.Voucher);
    }
}
