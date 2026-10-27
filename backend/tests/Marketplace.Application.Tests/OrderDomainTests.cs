using Marketplace.Domain;

namespace Marketplace.Application.Tests;

public class OrderDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 26);
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _supplier = Guid.NewGuid();

    private static PurchaseOrderLine Line(string name = "Coke 330ml", int cases = 2, long price = 24000, bool vat = true, int perCase = 24, Guid? product = null, string? barcode = "6001234567890") =>
        new(product ?? Guid.NewGuid(), name, barcode, perCase, cases, price, vat);

    private PurchaseOrder Order(IEnumerable<PurchaseOrderLine>? lines = null, long min = 0, long delivery = 0, OrderPaymentMethod pay = OrderPaymentMethod.OnDelivery) =>
        PurchaseOrder.Create(_shop, _supplier, Guid.NewGuid(), lines ?? [Line()], pay, min, delivery, Now).Value!;

    private static List<LineDecision> ConfirmAll(PurchaseOrder o) => o.Lines.Select(l => new LineDecision(l.Id, "confirm", null, null)).ToList();

    // ---- creating ----

    [Fact]
    public void A_new_order_is_a_draft_owing_with_its_lines_and_money_worked_out()
    {
        var order = Order([Line(cases: 2, price: 24000), Line("Fanta", cases: 1, price: 22000)], delivery: 5000);

        Assert.Equal((OrderStatus.Draft, OrderPaymentStatus.Owing), (order.Status, order.PaymentStatus));
        Assert.Equal(70000, order.SubtotalCents);                 // 2 x R240 + R220
        Assert.Equal(0, order.VatCents);                          // prices include VAT
        Assert.Equal(75000, order.TotalCents);                    // plus R50 delivery
        Assert.All(order.Lines, l => Assert.Equal(order.Id, l.OrderId));
    }

    [Fact]
    public void VAT_is_added_only_on_lines_whose_price_leaves_it_out()
    {
        var order = Order([Line(price: 10000, cases: 1, vat: false), Line("B", price: 10000, cases: 1, vat: true)]);

        Assert.Equal(1500, order.VatCents);
        Assert.Equal(21500, order.TotalCents);
    }

    [Fact]
    public void An_order_below_the_suppliers_minimum_is_flagged_but_still_allowed()
    {
        var small = Order([Line(cases: 1, price: 10000)], min: 50000);
        Assert.True(small.BelowMinimum);
        Assert.False(Order([Line(cases: 3, price: 20000)], min: 50000).BelowMinimum);
    }

    [Fact]
    public void An_order_needs_lines_sensible_quantities_and_prices_and_no_repeated_product()
    {
        string Code(params PurchaseOrderLine[] lines) => PurchaseOrder.Create(_shop, _supplier, Guid.NewGuid(), lines, OrderPaymentMethod.OnDelivery, 0, 0, Now).Error!.Code;
        var product = Guid.NewGuid();

        Assert.Equal("order.no_lines", Code());
        Assert.Equal("order.line_name", Code(Line(name: " ")));
        Assert.Equal("order.line_quantity", Code(Line(cases: 0)));
        Assert.Equal("order.line_quantity", Code(Line(cases: 10_001)));
        Assert.Equal("order.line_case_size", Code(Line(perCase: 0)));
        Assert.Equal("order.line_price", Code(Line(price: -1)));
        Assert.Equal("order.duplicate_line", Code(Line(product: product), Line(product: product)));
    }

    [Fact]
    public void Lines_for_a_supplier_off_the_platform_have_no_product_id_and_may_repeat_a_name()
    {
        var order = Order([new PurchaseOrderLine(null, "Loose sugar", null, 1, 5, 6000, true), new PurchaseOrderLine(null, "Loose sugar", null, 1, 1, 6000, true)]);
        Assert.Equal(2, order.Lines.Count);
    }

    // ---- sending and cancelling ----

    [Fact]
    public void A_draft_is_sent_once_by_the_chosen_channel()
    {
        var order = Order();

        Assert.True(order.Send(OrderChannel.Whatsapp, Now).IsSuccess);
        Assert.Equal((OrderStatus.Sent, OrderChannel.Whatsapp, Now), (order.Status, order.Channel, order.SentAt));
        Assert.Equal("order.already_sent", order.Send(OrderChannel.Email, Now).Error!.Code);
    }

    [Fact]
    public void A_shop_can_cancel_a_draft_or_a_sent_order_but_not_a_confirmed_one()
    {
        var draft = Order();
        Assert.True(draft.Cancel().IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, draft.Status);

        var sent = Order();
        sent.Send(OrderChannel.App, Now);
        Assert.True(sent.Cancel().IsSuccess);

        var confirmed = Order();
        confirmed.Send(OrderChannel.App, Now);
        confirmed.Respond(ConfirmAll(confirmed), Today.AddDays(2), Today, Now);
        Assert.Equal("order.cannot_cancel", confirmed.Cancel().Error!.Code);
    }

    [Fact]
    public void The_payment_choice_can_change_until_the_order_is_sent()
    {
        var order = Order();
        order.ChoosePayment(OrderPaymentMethod.InApp);
        Assert.Equal(OrderPaymentMethod.InApp, order.PaymentMethod);

        order.Send(OrderChannel.App, Now);
        order.ChoosePayment(OrderPaymentMethod.OnDelivery);
        Assert.Equal(OrderPaymentMethod.InApp, order.PaymentMethod);
    }

    // ---- the supplier replies ----

    private PurchaseOrder SentOrder()
    {
        var order = Order([Line("Coke", cases: 4, price: 24000), Line("Fanta", cases: 2, price: 22000)], delivery: 5000);
        order.Send(OrderChannel.App, Now);
        return order;
    }

    [Fact]
    public void Confirming_every_line_with_a_delivery_date_confirms_the_order()
    {
        var order = SentOrder();

        var result = order.Respond(ConfirmAll(order), Today.AddDays(2), Today, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal((OrderStatus.Confirmed, Today.AddDays(2)), (order.Status, order.DeliveryDate));
        Assert.False(order.HasChangesFromOrder);
        Assert.All(order.Lines, l => Assert.Equal(LineStatus.Confirmed, l.Status));
    }

    [Fact]
    public void Changing_a_quantity_or_price_and_declining_a_line_adjust_the_total_and_flag_the_changes_for_the_shop()
    {
        var order = SentOrder();
        var coke = order.Lines[0];
        var fanta = order.Lines[1];

        order.Respond([new(coke.Id, "change", 3, 24500), new(fanta.Id, "decline", null, null)], Today.AddDays(1), Today, Now);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal((LineStatus.Changed, 3, 24500L), (coke.Status, coke.EffectiveCases, coke.CasePriceCents));
        Assert.Equal((LineStatus.Declined, 0), (fanta.Status, fanta.EffectiveCases));
        Assert.Equal(73500, order.SubtotalCents);                // 3 x R245
        Assert.Equal(78500, order.TotalCents);                   // plus delivery
        Assert.True(order.HasChangesFromOrder);
    }

    [Fact]
    public void Declining_every_line_cancels_the_order_with_no_delivery_date_needed()
    {
        var order = SentOrder();

        var result = order.Respond(order.Lines.Select(l => new LineDecision(l.Id, "decline", null, null)).ToList(), null, Today, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(0, order.TotalCents);                       // no delivery fee for nothing
    }

    [Fact]
    public void A_reply_must_cover_every_line_use_known_actions_and_a_valid_delivery_date()
    {
        var order = SentOrder();
        var all = ConfirmAll(order);
        string Code(List<LineDecision> d, DateOnly? date) => order.Respond(d, date, Today, Now).Error!.Code;

        Assert.Equal("order.decision_missing", Code(all.Take(1).ToList(), Today.AddDays(2)));
        Assert.Equal("order.decision_missing", Code([.. all, new(Guid.NewGuid(), "confirm", null, null)], Today.AddDays(2)));
        Assert.Equal("order.action_invalid", Code([new(all[0].LineId, "maybe", null, null), all[1]], Today.AddDays(2)));
        Assert.Equal("order.change_invalid", Code([new(all[0].LineId, "change", 0, null), all[1]], Today.AddDays(2)));
        Assert.Equal("order.change_invalid", Code([new(all[0].LineId, "change", 2, -5), all[1]], Today.AddDays(2)));
        Assert.Equal("order.delivery_date_required", Code(all, null));
        Assert.Equal("order.delivery_date_invalid", Code(all, Today));
        Assert.Equal("order.delivery_date_invalid", Code(all, Today.AddDays(31)));
        Assert.Equal(OrderStatus.Sent, order.Status);            // nothing changed by the failed attempts
        Assert.All(order.Lines, l => Assert.Equal(LineStatus.Pending, l.Status));
    }

    [Fact]
    public void Only_a_sent_order_can_be_replied_to()
    {
        var draft = Order();
        Assert.Equal("order.not_awaiting_reply", draft.Respond(ConfirmAll(draft), Today.AddDays(2), Today, Now).Error!.Code);
    }

    // ---- delivery and receiving ----

    private PurchaseOrder ConfirmedOrder()
    {
        var order = SentOrder();
        order.Respond(ConfirmAll(order), Today.AddDays(2), Today, Now);
        return order;
    }

    [Fact]
    public void The_supplier_marks_it_delivered_and_lines_not_mentioned_were_delivered_in_full()
    {
        var order = ConfirmedOrder();

        var result = order.MarkDelivered(new Dictionary<Guid, int> { [order.Lines[0].Id] = 3 }, Now);   // 3 of the 4 Coke cases

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal((3, 2), (order.Lines[0].EffectiveCases, order.Lines[1].EffectiveCases));
        Assert.Equal(3 * 24000 + 2 * 22000 + 5000, order.TotalCents);   // the short case is not charged
    }

    [Fact]
    public void Delivered_cases_cannot_exceed_what_was_confirmed_or_be_negative_and_only_a_confirmed_order_can_be_delivered()
    {
        var order = ConfirmedOrder();
        Assert.Equal("order.delivered_invalid", order.MarkDelivered(new Dictionary<Guid, int> { [order.Lines[0].Id] = 5 }, Now).Error!.Code);
        Assert.Equal("order.delivered_invalid", order.MarkDelivered(new Dictionary<Guid, int> { [order.Lines[0].Id] = -1 }, Now).Error!.Code);
        Assert.Equal("order.line_unknown", order.MarkDelivered(new Dictionary<Guid, int> { [Guid.NewGuid()] = 1 }, Now).Error!.Code);
        Assert.Equal(OrderStatus.Confirmed, order.Status);

        Assert.Equal("order.not_confirmed", SentOrder().MarkDelivered(new Dictionary<Guid, int>(), Now).Error!.Code);
    }

    [Fact]
    public void The_shops_count_wins_so_a_short_delivery_only_charges_for_what_arrived()
    {
        var order = ConfirmedOrder();
        order.MarkDelivered(new Dictionary<Guid, int>(), Now);                                   // supplier says all delivered

        var result = order.Receive(new Dictionary<Guid, int> { [order.Lines[1].Id] = 1 }, Now.AddHours(3));   // shop counted 1 of 2 Fanta

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddHours(3), order.ReceivedAt);
        Assert.Equal((4, 1), (order.Lines[0].EffectiveCases, order.Lines[1].EffectiveCases));
        Assert.Equal(4 * 24000 + 22000 + 5000, order.TotalCents);
        Assert.Equal("order.already_received", order.Receive(null, Now).Error!.Code);
    }

    [Fact]
    public void Receiving_before_the_supplier_marks_it_delivered_still_completes_the_order()
    {
        var order = ConfirmedOrder();

        Assert.True(order.Receive(null, Now.AddDays(2)).IsSuccess);

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(Now.AddDays(2), order.DeliveredAt);
        Assert.Equal((4, 2), (order.Lines[0].EffectiveCases, order.Lines[1].EffectiveCases));
    }

    [Fact]
    public void An_order_that_is_not_confirmed_cannot_be_received()
    {
        Assert.Equal("order.not_delivered", SentOrder().Receive(null, Now).Error!.Code);
        Assert.Equal("order.not_delivered", Order().Receive(null, Now).Error!.Code);
    }

    [Fact]
    public void A_delivered_order_can_be_disputed_and_payment_status_can_move()
    {
        var order = ConfirmedOrder();
        order.MarkDisputed();
        Assert.Equal(OrderStatus.Confirmed, order.Status);        // only a delivered order can be disputed

        order.MarkDelivered(new Dictionary<Guid, int>(), Now);
        order.MarkDisputed();
        Assert.Equal(OrderStatus.Disputed, order.Status);

        order.SetPaymentStatus(OrderPaymentStatus.Held);
        Assert.Equal(OrderPaymentStatus.Held, order.PaymentStatus);
    }

    [Fact]
    public void A_price_agreed_at_ordering_is_not_rewritten_by_a_later_catalogue_change()
    {
        var order = Order([Line(price: 24000, cases: 1)]);
        // The line carries its own copy of name and price; nothing here refers back to the supplier's product.
        Assert.Equal(24000, order.Lines[0].CasePriceCents);
        Assert.Equal("Coke 330ml", order.Lines[0].Name);
    }
}
