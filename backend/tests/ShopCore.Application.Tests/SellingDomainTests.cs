using ShopCore.Domain;
using ShopCore.Domain.Selling;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public class TicketTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid TillA = Guid.NewGuid();
    private static readonly Guid TillB = Guid.NewGuid();
    private static readonly Guid Burger = Guid.NewGuid();
    private static readonly Guid Cola = Guid.NewGuid();

    private static OpenTicket Ticket(string label = "Table 4", params TicketLine[] lines) =>
        OpenTicket.Open(Guid.NewGuid(), Shop, Staff, TillA, label, null, lines.Length == 0 ? [new TicketLine(Burger, "Burger", 2, 4_500)] : lines, Now).Value!;

    [Fact]
    public void A_new_ticket_is_held_by_the_till_that_opened_it_and_works_out_its_total()
    {
        var ticket = Ticket("Anna", new TicketLine(Burger, "Burger", 2, 4_500), new TicketLine(Cola, "Cola", 1, 1_500));

        Assert.Equal((TicketStatus.Open, TillA, 10_500L), (ticket.Status, ticket.HeldByDeviceId!.Value, ticket.TotalCents));
        Assert.Equal(Now.AddMinutes(10), ticket.HeldUntil);
    }

    [Fact]
    public void A_ticket_needs_a_label_and_sensible_lines()
    {
        Assert.Equal("ticket.label_required", OpenTicket.Open(Guid.NewGuid(), Shop, Staff, TillA, " ", null, [], Now).Error!.Code);
        Assert.Equal("ticket.line_quantity", OpenTicket.Open(Guid.NewGuid(), Shop, Staff, TillA, "T1", null, [new TicketLine(Burger, "Burger", 0, 100)], Now).Error!.Code);
        Assert.Equal("ticket.line_price", OpenTicket.Open(Guid.NewGuid(), Shop, Staff, TillA, "T1", null, [new TicketLine(Burger, "Burger", 1, -1)], Now).Error!.Code);
        Assert.Equal("ticket.line_name", OpenTicket.Open(Guid.NewGuid(), Shop, Staff, TillA, "T1", null, [new TicketLine(Burger, " ", 1, 100)], Now).Error!.Code);
        Assert.Equal("ticket.line_unit", OpenTicket.Open(Guid.NewGuid(), Shop, Staff, TillA, "T1", null, [new TicketLine(Burger, "Burger", 1, 100, 7)], Now).Error!.Code);
        Assert.Equal("ticket.too_many_lines", OpenTicket.Open(Guid.NewGuid(), Shop, Staff, TillA, "T1", null, [.. Enumerable.Range(0, 101).Select(_ => new TicketLine(Guid.NewGuid(), "x", 1, 1))], Now).Error!.Code);
    }

    [Fact]
    public void Another_till_cannot_take_a_ticket_that_is_held_until_the_hold_lapses_after_ten_minutes()
    {
        var ticket = Ticket();

        Assert.Equal("ticket.locked", ticket.Hold(TillB, Now.AddMinutes(9)).Error!.Code);
        Assert.True(ticket.Hold(TillB, Now.AddMinutes(10)).IsSuccess);        // lapsed
        Assert.Equal(TillB, ticket.HeldByDeviceId);
        Assert.Equal("ticket.locked", ticket.Hold(TillA, Now.AddMinutes(11)).Error!.Code);   // and now A is locked out
    }

    [Fact]
    public void The_holding_till_can_hold_again_and_releasing_frees_it_for_others()
    {
        var ticket = Ticket();

        Assert.True(ticket.Hold(TillA, Now.AddMinutes(5)).IsSuccess);
        Assert.Equal(Now.AddMinutes(15), ticket.HeldUntil);
        ticket.Release(TillB);                                    // not the holder: nothing happens
        Assert.Equal(TillA, ticket.HeldByDeviceId);
        ticket.Release(TillA);
        Assert.True(ticket.Hold(TillB, Now.AddMinutes(6)).IsSuccess);
    }

    [Fact]
    public void Only_the_holder_can_edit_and_an_expired_hold_cannot()
    {
        var ticket = Ticket();
        var more = new[] { new TicketLine(Burger, "Burger", 3, 4_500) };

        Assert.Equal("ticket.locked", ticket.Edit(TillB, more, Now.AddMinutes(1)).Error!.Code);
        Assert.True(ticket.Edit(TillA, more, Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(13_500, ticket.TotalCents);
        Assert.Equal("ticket.not_held", ticket.Edit(TillA, more, Now.AddMinutes(30)).Error!.Code);   // A's refreshed hold ran out
    }

    [Fact]
    public void A_ticket_that_nobody_holds_must_be_opened_before_it_is_edited()
    {
        var ticket = Ticket();
        ticket.Release(TillA);

        Assert.Equal("ticket.not_held", ticket.Edit(TillA, [new TicketLine(Burger, "Burger", 1, 4_500)], Now).Error!.Code);
    }

    [Fact]
    public void Merging_joins_matching_lines_and_voids_the_other_ticket()
    {
        var one = Ticket("T1", new TicketLine(Burger, "Burger", 2, 4_500), new TicketLine(Cola, "Cola", 1, 1_500));
        var two = Ticket("T2", new TicketLine(Burger, "Burger", 1, 4_500), new TicketLine(Cola, "Cola", 1, 1_500, 1, "no ice"));

        var merged = one.MergeIn(two, TillA, Now);

        Assert.True(merged.IsSuccess);
        Assert.Equal(TicketStatus.Voided, two.Status);
        Assert.Equal([(Burger, 3), (Cola, 1), (Cola, 1)], one.Lines.Select(l => (l.ProductId, l.Quantity)));   // the note keeps the second cola apart
        Assert.Equal(3 * 4_500 + 2 * 1_500, one.TotalCents);
    }

    [Fact]
    public void Add_ons_keep_otherwise_identical_items_apart()
    {
        var cheese = Guid.NewGuid();
        var plain = new TicketLine(Burger, "Burger", 1, 4_500);
        var withCheese = new TicketLine(Burger, "Burger", 1, 5_000, 1, null, [cheese]);

        Assert.NotEqual(plain.Key, withCheese.Key);
        Assert.Equal(withCheese.Key, (withCheese with { Quantity = 3 }).Key);
    }

    [Fact]
    public void A_ticket_cannot_merge_into_itself_or_with_one_the_till_does_not_hold()
    {
        var one = Ticket("T1");
        var two = Ticket("T2");
        two.Release(TillA);
        two.Hold(TillB, Now);

        Assert.Equal("ticket.merge_self", one.MergeIn(one, TillA, Now).Error!.Code);
        Assert.Equal("ticket.locked", one.MergeIn(two, TillA, Now).Error!.Code);
        Assert.Equal(TicketStatus.Open, two.Status);
    }

    [Fact]
    public void Closing_ties_the_ticket_to_its_sale_once_and_a_closed_ticket_cannot_be_used()
    {
        var ticket = Ticket();
        var sale = Guid.NewGuid();

        Assert.True(ticket.Close(sale).IsSuccess);

        Assert.Equal((TicketStatus.Closed, sale, (Guid?)null), (ticket.Status, ticket.ClosedSaleId!.Value, ticket.HeldByDeviceId));
        Assert.Equal("ticket.not_open", ticket.Close(Guid.NewGuid()).Error!.Code);
        Assert.Equal("ticket.not_open", ticket.Hold(TillA, Now).Error!.Code);
        Assert.Equal("ticket.not_open", ticket.Edit(TillA, [], Now).Error!.Code);
    }

    [Fact]
    public void Moving_to_another_table_needs_the_hold()
    {
        var ticket = Ticket();
        var table = Guid.NewGuid();

        Assert.Equal("ticket.locked", ticket.MoveToTable(TillB, table, Now).Error!.Code);
        Assert.True(ticket.MoveToTable(TillA, table, Now).IsSuccess);
        Assert.Equal(table, ticket.TableId);
    }
}

public class KitchenOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static KitchenOrder Order(string no = "A-17") =>
        KitchenOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), no, null, null, "Table 4", [new KitchenItem("Burger", 2, "no onion")], Now).Value!;

    [Fact]
    public void An_order_number_is_upper_case_letters_digits_and_dashes_made_with_the_tills_prefix()
    {
        Assert.Equal("A-17", Order("a-17").OrderNo);
        Assert.Equal("kitchen.order_no_invalid", KitchenOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "no spaces", null, null, null, [new("x", 1)], Now).Error!.Code);
        Assert.Equal("kitchen.order_no_invalid", KitchenOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "", null, null, null, [new("x", 1)], Now).Error!.Code);
        Assert.Equal("kitchen.items_invalid", KitchenOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "A-1", null, null, null, [], Now).Error!.Code);
        Assert.Equal("kitchen.items_invalid", KitchenOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "A-1", null, null, null, [new("x", 100)], Now).Error!.Code);
    }

    [Fact]
    public void An_order_goes_new_then_preparing_then_ready_then_collected_and_not_backwards()
    {
        var order = Order();

        Assert.Equal("kitchen.not_ready", order.Collect().Error!.Code);
        Assert.True(order.Start().IsSuccess);
        Assert.Equal("kitchen.not_new", order.Start().Error!.Code);
        Assert.True(order.MarkReady(Now.AddMinutes(8)).IsSuccess);
        Assert.Equal(Now.AddMinutes(8), order.ReadyAt);
        Assert.True(order.Collect().IsSuccess);
        Assert.Equal("kitchen.not_in_progress", order.MarkReady(Now).Error!.Code);
        Assert.Equal("kitchen.not_cancellable", order.Cancel().Error!.Code);
    }

    [Fact]
    public void A_cook_can_mark_a_new_order_ready_without_starting_it_and_an_order_can_be_cancelled_until_collected()
    {
        var quick = Order();
        var cancelled = Order("A-18");

        Assert.True(quick.MarkReady(Now).IsSuccess);
        Assert.True(cancelled.Cancel().IsSuccess);
        Assert.Equal("kitchen.not_cancellable", cancelled.Cancel().Error!.Code);
    }
}

public class QuoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static Result<Quote> Make(DateOnly? expires = null, params TicketLine[] lines) =>
        Quote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Q-001", "Mr Naidoo", "0821234567", null,
            lines.Length == 0 ? [new TicketLine(Guid.NewGuid(), "Paint 20l", 3, 85_000), new TicketLine(Guid.NewGuid(), "Brush", 2, 3_500)] : lines, expires ?? Today.AddDays(14), Now);

    [Fact]
    public void A_quotes_total_is_worked_out_from_its_lines()
    {
        Assert.Equal(262_000, Make().Value!.TotalCents);
    }

    [Fact]
    public void A_quote_is_valid_from_today_for_at_most_90_days_and_needs_real_lines()
    {
        Assert.Equal("quote.expiry_invalid", Make(Today.AddDays(-1)).Error!.Code);
        Assert.Equal("quote.expiry_invalid", Make(Today.AddDays(91)).Error!.Code);
        Assert.True(Make(Today.AddDays(90)).IsSuccess);
        Assert.True(Make(Today).IsSuccess);
        Assert.Equal("quote.lines_invalid", Quote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Q", null, null, null, [], Today.AddDays(1), Now).Error!.Code);
        Assert.Equal("quote.number_invalid", Quote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " ", null, null, null, [new TicketLine(Guid.NewGuid(), "x", 1, 1)], Today.AddDays(1), Now).Error!.Code);
    }

    [Fact]
    public void An_open_quote_past_its_date_reads_as_expired_and_cannot_be_converted()
    {
        var quote = Make(Today.AddDays(7)).Value!;

        Assert.Equal(QuoteStatus.Open, quote.StatusOn(Today.AddDays(7)));
        Assert.Equal(QuoteStatus.Expired, quote.StatusOn(Today.AddDays(8)));
        Assert.Equal("quote.expired", quote.Convert(Guid.NewGuid(), Today.AddDays(8)).Error!.Code);
        Assert.Equal(QuoteStatus.Open, quote.Status);   // reading it never changed it
    }

    [Fact]
    public void A_quote_converts_once_and_a_cancelled_quote_cannot()
    {
        var quote = Make().Value!;
        var ticket = Guid.NewGuid();

        Assert.True(quote.Convert(ticket, Today).IsSuccess);
        Assert.Equal((QuoteStatus.Converted, ticket), (quote.Status, quote.TicketId!.Value));
        Assert.Equal("quote.not_open", quote.Convert(Guid.NewGuid(), Today).Error!.Code);
        Assert.Equal("quote.not_open", quote.Cancel().Error!.Code);

        var other = Make().Value!;
        other.Cancel();
        Assert.Equal("quote.not_open", other.Convert(Guid.NewGuid(), Today).Error!.Code);
    }
}

public class LaybyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly LaybyTerms Terms = new();
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid Till = Guid.NewGuid();

    private static Result<LaybyPlan> Make(long deposit = 20_000, DateOnly? due = null, string method = "cash", LaybyTerms? terms = null) =>
        LaybyPlan.Create(Guid.NewGuid(), Guid.NewGuid(), Staff, Till, "L-001", "Mrs Khumalo", "0821110000", null,
            [new TicketLine(Guid.NewGuid(), "Jacket", 1, 100_000)], deposit, method, due ?? Today.AddDays(28), terms ?? Terms, Now);

    [Fact]
    public void A_lay_by_starts_with_a_deposit_of_at_least_the_shops_percentage()
    {
        var plan = Make(20_000).Value!;

        Assert.Equal((LaybyStatus.Active, 100_000L, 20_000L, 80_000L), (plan.Status, plan.TotalCents, plan.PaidCents, plan.BalanceCents));
        Assert.True(plan.ReservesStock);
        Assert.Equal("layby.deposit_too_small", Make(19_999).Error!.Code);
        Assert.Equal("layby.deposit_too_large", Make(100_000).Error!.Code);
        Assert.Equal("layby.deposit_too_small", Make(0, terms: new LaybyTerms(0)).Error!.Code);
        Assert.True(Make(1_000, terms: new LaybyTerms(1)).IsSuccess);
    }

    [Fact]
    public void The_minimum_deposit_is_rounded_up_so_it_is_never_below_the_percentage()
    {
        var odd = LaybyPlan.Create(Guid.NewGuid(), Guid.NewGuid(), Staff, Till, "L", "Mrs K", null, null, [new TicketLine(Guid.NewGuid(), "x", 1, 10_001)], 2_000, "cash", Today.AddDays(7), Terms, Now);

        Assert.Equal("layby.deposit_too_small", odd.Error!.Code);   // 20% of 100.01 is 20.002, so R20.00 is not enough
        Assert.True(LaybyPlan.Create(Guid.NewGuid(), Guid.NewGuid(), Staff, Till, "L", "Mrs K", null, null, [new TicketLine(Guid.NewGuid(), "x", 1, 10_001)], 2_001, "cash", Today.AddDays(7), Terms, Now).IsSuccess);
    }

    [Fact]
    public void It_must_be_paid_off_within_the_shops_limit_and_needs_a_customer_and_a_real_method()
    {
        Assert.Equal("layby.due_invalid", Make(due: Today).Error!.Code);
        Assert.Equal("layby.due_invalid", Make(due: Today.AddDays(85)).Error!.Code);   // 12 weeks is 84 days
        Assert.True(Make(due: Today.AddDays(84)).IsSuccess);
        Assert.Equal("layby.method_invalid", Make(method: "credit").Error!.Code);
        Assert.Equal("layby.customer_required", LaybyPlan.Create(Guid.NewGuid(), Guid.NewGuid(), Staff, Till, "L", " ", null, null, [new TicketLine(Guid.NewGuid(), "x", 1, 10_000)], 3_000, "cash", Today.AddDays(7), Terms, Now).Error!.Code);
    }

    [Fact]
    public void Payments_reduce_the_balance_cannot_overpay_and_the_last_one_completes_the_plan()
    {
        var plan = Make().Value!;

        Assert.True(plan.Pay(30_000, "card", Staff, Till, Now.AddDays(7)).IsSuccess);
        Assert.Equal("layby.overpaid", plan.Pay(50_001, "cash", Staff, Till, Now).Error!.Code);
        Assert.Equal("layby.amount_invalid", plan.Pay(0, "cash", Staff, Till, Now).Error!.Code);
        Assert.Equal(LaybyStatus.Active, plan.Status);

        Assert.True(plan.Pay(50_000, "cash", Staff, Till, Now.AddDays(14)).IsSuccess);

        Assert.Equal((LaybyStatus.Completed, 0L, 3), (plan.Status, plan.BalanceCents, plan.Payments.Count));
        Assert.Equal("layby.not_active", plan.Pay(1, "cash", Staff, Till, Now).Error!.Code);
        Assert.True(plan.ReservesStock);   // still set aside until collected
    }

    [Fact]
    public void Cancelling_keeps_the_fee_and_gives_back_the_rest()
    {
        var plan = Make(40_000).Value!;   // R400 paid of R1000; fee is 10% = R100

        Assert.True(plan.Cancel(Terms).IsSuccess);

        Assert.Equal((LaybyStatus.Cancelled, 10_000L, 30_000L), (plan.Status, plan.CancellationFeeCents, plan.RefundDueCents));
        Assert.False(plan.ReservesStock);
        Assert.Equal("layby.not_active", plan.Cancel(Terms).Error!.Code);
    }

    [Fact]
    public void The_fee_is_at_least_the_minimum_but_never_more_than_was_paid()
    {
        var small = LaybyPlan.Create(Guid.NewGuid(), Guid.NewGuid(), Staff, Till, "L", "Mrs K", null, null, [new TicketLine(Guid.NewGuid(), "x", 1, 10_000)], 2_000, "cash", Today.AddDays(7), Terms, Now).Value!;

        Assert.Equal(2_000, small.FeeFor(Terms));                                            // 10% is R10 but the minimum is R20, and R20 was all that was paid
        Assert.Equal(2_000, small.FeeFor(new LaybyTerms(CancelFeePercent: 50, MinCancelFeeCents: 100)) );   // 50% would be R50: capped at what was paid
        Assert.Equal(1_000, small.FeeFor(new LaybyTerms(CancelFeePercent: 10, MinCancelFeeCents: 0)));
    }

    [Fact]
    public void A_plan_not_paid_off_by_its_due_date_expires_on_the_same_terms()
    {
        var plan = Make(40_000, Today.AddDays(28)).Value!;

        Assert.False(plan.ExpireIfDue(Today.AddDays(28), Terms));    // due day itself is still fine
        Assert.True(plan.ExpireIfDue(Today.AddDays(29), Terms));
        Assert.Equal((LaybyStatus.Expired, 30_000L), (plan.Status, plan.RefundDueCents));
        Assert.False(plan.ExpireIfDue(Today.AddDays(40), Terms));
    }

    [Fact]
    public void A_paid_off_plan_is_collected_once_and_nothing_else_can_be_collected()
    {
        var plan = Make().Value!;
        Assert.Equal("layby.not_complete", plan.Collect(Guid.NewGuid()).Error!.Code);
        plan.Pay(80_000, "cash", Staff, Till, Now);

        var sale = Guid.NewGuid();
        Assert.True(plan.Collect(sale).IsSuccess);
        Assert.Equal((LaybyStatus.Collected, sale), (plan.Status, plan.SaleId!.Value));
        Assert.False(plan.ReservesStock);
        Assert.Equal("layby.not_complete", plan.Collect(Guid.NewGuid()).Error!.Code);
    }

    [Fact]
    public void Terms_come_from_the_pack_settings_with_defaults_for_anything_missing_or_silly()
    {
        Assert.Equal(new LaybyTerms(), LaybyTerms.FromJson(null));
        Assert.Equal(new LaybyTerms(30, 8, 15, 5_000), LaybyTerms.FromJson("{\"minDepositPercent\":30,\"maxWeeks\":8,\"cancelFeePercent\":15,\"minCancelFeeCents\":5000}"));
        Assert.Equal(new LaybyTerms(20, 12, 10, 2_000), LaybyTerms.FromJson("{\"minDepositPercent\":500,\"maxWeeks\":0,\"cancelFeePercent\":-1,\"minCancelFeeCents\":-5}"));
        Assert.Equal(new LaybyTerms(), LaybyTerms.FromJson("not json"));
    }
}

public class VoucherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Staff = Guid.NewGuid();

    private static (GiftVoucher Voucher, string Code) Issue(long cents = 50_000, int months = 12) =>
        GiftVoucher.Issue(Guid.NewGuid(), cents, months, Staff, Guid.NewGuid(), null, "issue-1", Now).Value;

    [Fact]
    public void A_voucher_is_issued_with_a_code_shown_once_and_only_its_hash_is_kept()
    {
        var (voucher, code) = Issue();

        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", code);
        Assert.DoesNotContain("I", code.Replace("-", ""));
        Assert.Equal((50_000L, GiftVoucher.Normalise(code)[^4..]), (voucher.BalanceCents, voucher.CodeLast4));
        Assert.Equal(GiftVoucher.Hash(code), voucher.CodeHash);
        Assert.Equal(GiftVoucher.Hash(code), GiftVoucher.Hash(code.ToLowerInvariant().Replace("-", " ")));
        Assert.DoesNotContain(GiftVoucher.Normalise(code), voucher.CodeHash, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new DateOnly(2027, 10, 26), voucher.ExpiresOn);
    }

    [Fact]
    public void Voucher_amount_and_validity_are_bounded_and_codes_do_not_repeat()
    {
        Assert.Equal("voucher.amount_invalid", GiftVoucher.Issue(Guid.NewGuid(), 0, 12, Staff, Guid.NewGuid(), null, "r", Now).Error!.Code);
        Assert.Equal("voucher.amount_invalid", GiftVoucher.Issue(Guid.NewGuid(), GiftVoucher.MaxCents + 1, 12, Staff, Guid.NewGuid(), null, "r", Now).Error!.Code);
        Assert.Equal("voucher.validity_invalid", GiftVoucher.Issue(Guid.NewGuid(), 100, 0, Staff, Guid.NewGuid(), null, "r", Now).Error!.Code);
        Assert.Equal(500, Enumerable.Range(0, 500).Select(_ => GiftVoucher.NewCode()).Distinct().Count());
    }

    [Fact]
    public void Redeeming_takes_value_off_and_a_partial_redemption_leaves_the_rest()
    {
        var (voucher, _) = Issue();

        Assert.True(voucher.Redeem(20_000, Guid.NewGuid(), "sale-1", Staff, Now).IsSuccess);
        Assert.Equal(30_000, voucher.BalanceCents);
        Assert.Equal("voucher.insufficient", voucher.Redeem(30_001, Guid.NewGuid(), "sale-2", Staff, Now).Error!.Code);
        Assert.True(voucher.Redeem(30_000, null, "sale-3", Staff, Now).IsSuccess);
        Assert.Equal(0, voucher.BalanceCents);
    }

    [Fact]
    public void The_same_redemption_twice_takes_the_value_once()
    {
        var (voucher, _) = Issue();

        voucher.Redeem(20_000, null, "till-event-9", Staff, Now);
        voucher.Redeem(20_000, null, "till-event-9", Staff, Now);

        Assert.Equal(30_000, voucher.BalanceCents);
        Assert.Equal(2, voucher.Entries.Count);
    }

    [Fact]
    public void An_expired_or_cancelled_voucher_cannot_be_used_and_the_last_day_still_works()
    {
        var (voucher, _) = Issue(10_000, 1);   // valid to 26 November

        Assert.True(voucher.Redeem(1_000, null, "a", Staff, new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero)).IsSuccess);
        Assert.Equal("voucher.expired", voucher.Redeem(1_000, null, "b", Staff, new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero)).Error!.Code);

        var (other, _) = Issue();
        other.Cancel();
        Assert.Equal("voucher.void", other.Redeem(1, null, "c", Staff, Now).Error!.Code);
        Assert.Equal("voucher.amount_invalid", Issue().Voucher.Redeem(0, null, "d", Staff, Now).Error!.Code);
    }
}

public class SerialNumberTests
{
    [Fact]
    public void A_serial_is_cleaned_to_upper_case_and_must_be_a_sensible_length_and_shape()
    {
        Assert.Equal("SN-12345/A", SerialNumberRecord.Clean(" sn-12345/a ").Value);
        Assert.Equal("serial.invalid", SerialNumberRecord.Clean("ab").Error!.Code);
        Assert.Equal("serial.invalid", SerialNumberRecord.Clean(new string('9', 51)).Error!.Code);
        Assert.Equal("serial.invalid", SerialNumberRecord.Clean("SN 123 456").Error!.Code);
        Assert.Equal("serial.invalid", SerialNumberRecord.Clean(null).Error!.Code);
    }
}

public class VariantAndFloorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static Product Parent() => Product.Record(new ProductInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Cotton tee", null, "clothing", 8_000, 19_900, 3, null, true, Now));

    [Fact]
    public void A_variant_is_a_product_of_its_own_under_its_parent_with_its_own_barcode_price_and_cost()
    {
        var parent = Parent();

        var variant = Product.RecordVariant(parent, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "M / Blue", "6001234500012", 8_500, 21_900, Now).Value!;

        Assert.Equal((parent.Id, "Cotton tee - M / Blue", "M / Blue", "clothing", "6001234500012", 21_900L), (variant.ParentProductId!.Value, variant.Name, variant.VariantLabel, variant.CategoryCode, variant.Barcode, variant.PriceCents));
        Assert.Equal(parent.ShopId, variant.ShopId);
        Assert.NotEqual(parent.Id, variant.Id);
    }

    [Fact]
    public void A_variant_needs_a_label_a_valid_barcode_and_cannot_have_variants_itself()
    {
        var parent = Parent();
        var variant = Product.RecordVariant(parent, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "L", null, 0, 100, Now).Value!;

        Assert.Equal("variant.label_invalid", Product.RecordVariant(parent, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " ", null, 0, 100, Now).Error!.Code);
        Assert.Equal("variant.label_invalid", Product.RecordVariant(parent, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('x', 61), null, 0, 100, Now).Error!.Code);
        Assert.Equal("product.barcode_invalid", Product.RecordVariant(parent, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "XL", "12", 0, 100, Now).Error!.Code);
        Assert.Equal("product.price_invalid", Product.RecordVariant(parent, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "XL", null, 0, -1, Now).Error!.Code);
        Assert.Equal("variant.nested", Product.RecordVariant(variant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "XXL", null, 0, 100, Now).Error!.Code);
    }

    [Fact]
    public void A_floor_plan_has_named_tables_in_areas_with_seats_and_no_duplicates()
    {
        Assert.True(FloorTable.Validate([new("Inside", "T1", 4), new("Inside", "T2", 2), new("Patio", "T1", 6)]).IsSuccess);
        Assert.Equal("floor.duplicate", FloorTable.Validate([new("Inside", "T1", 4), new(" inside", "t1 ", 2)]).Error!.Code);
        Assert.Equal("floor.name_invalid", FloorTable.Validate([new("Inside", " ", 4)]).Error!.Code);
        Assert.Equal("floor.area_invalid", FloorTable.Validate([new("", "T1", 4)]).Error!.Code);
        Assert.Equal("floor.seats_invalid", FloorTable.Validate([new("Inside", "T1", 0)]).Error!.Code);
        Assert.Equal("floor.too_many", FloorTable.Validate([.. Enumerable.Range(0, 201).Select(i => new FloorTable.Input("A", $"T{i}", 2))]).Error!.Code);
    }

    [Fact]
    public void Modifier_groups_keep_their_options_in_order_and_check_their_limits()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var group = ModifierGroup.Create(Guid.NewGuid(), "Extras", false, 0, 2, [a, b], Now).Value!;

        Assert.Equal([a, b], group.Options.Select(o => o.ProductId));
        Assert.Equal("modifier.limits_invalid", ModifierGroup.Create(Guid.NewGuid(), "Extras", false, 0, 3, [a, b], Now).Error!.Code);   // maximum above the options
        Assert.Equal("modifier.option_repeated", ModifierGroup.Create(Guid.NewGuid(), "Extras", false, 0, 1, [a, a], Now).Error!.Code);
        Assert.True(group.Change("Sauces", true, 1, 1, [b]).IsSuccess);
        Assert.Equal([b], group.Options.Select(o => o.ProductId));
        Assert.Equal(2, group.Version);
    }

    [Fact]
    public void A_stored_promotion_becomes_the_engine_definition_and_checks_its_hours()
    {
        var product = Guid.NewGuid();
        var input = new Promotion.Input("Happy hour", PromotionType.PercentOff, [product], 20, 0, [], Now, Now.AddDays(30), Weekdays.All, new TimeOnly(16, 0), new TimeOnly(18, 0));

        var promotion = Promotion.Create(Guid.NewGuid(), input, Now).Value!;
        var def = promotion.ToDef();

        Assert.Equal((promotion.Id, PromotionType.PercentOff, 20, new TimeOnly(16, 0)), (def.Id, def.Type, def.Percent, def.From!.Value));
        Assert.Equal([product], def.ProductIds);
        Assert.Equal("promotion.hours_invalid", Promotion.Create(Guid.NewGuid(), input with { To = null }, Now).Error!.Code);
        Assert.Equal("promotion.hours_invalid", Promotion.Create(Guid.NewGuid(), input with { To = new TimeOnly(16, 0) }, Now).Error!.Code);
        promotion.SetActive(false);
        Assert.False(promotion.ToDef().IsActive);
    }
}
