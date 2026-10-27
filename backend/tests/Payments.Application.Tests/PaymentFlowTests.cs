using TillKnowIT.Security;
using Identity.Contracts;
using Marketplace.Contracts;
using Microsoft.Extensions.Time.Testing;
using Payments.Contracts;
using Payments.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Payments.Application.Tests;

public class PaymentFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private const string Secret = "shh";

    private sealed class FakeOrders : IOrderPayments
    {
        public Dictionary<Guid, OrderPaymentInfo> Orders { get; } = [];
        public Task<OrderPaymentInfo?> Get(Guid id, CancellationToken ct) => Task.FromResult(Orders.GetValueOrDefault(id));

        public Task<Result> SetPaymentStatus(Guid id, string status, CancellationToken ct)
        {
            Orders[id] = Orders[id] with { PaymentStatus = status };
            return Task.FromResult(Result.Ok());
        }

        public Task<Result> SetDisputed(Guid id, bool disputed, CancellationToken ct)
        {
            var o = Orders[id];
            if (disputed && o.Status != "delivered") return Task.FromResult(Result.Fail("order.not_delivered", "Only a delivered order can be disputed."));
            Orders[id] = o with { Status = disputed ? "disputed" : "delivered" };
            return Task.FromResult(Result.Ok());
        }
    }

    private sealed class FakePayments : IPaymentStore
    {
        public List<Payment> Payments { get; } = [];
        public List<WebhookReceipt> Receipts { get; } = [];
        public List<ReconciliationRun> Runs { get; } = [];
        public Task Add(Payment p, CancellationToken ct) { Payments.Add(p); return Task.CompletedTask; }
        public Task<Payment?> Get(Guid id, CancellationToken ct) => Task.FromResult(Payments.FirstOrDefault(p => p.Id == id));
        public Task<Payment?> ForOrder(Guid orderId, CancellationToken ct) => Task.FromResult(Payments.Where(p => p.OrderId == orderId && p.Status != PaymentStatus.Failed).OrderByDescending(p => p.CreatedAt).FirstOrDefault());
        public Task<Payment?> ByPartnerRef(string r, CancellationToken ct) => Task.FromResult(Payments.FirstOrDefault(p => p.PartnerRef == r));
        public Task<List<Payment>> DueForRelease(DateTimeOffset now, int take, CancellationToken ct) => Task.FromResult(Payments.Where(p => p.IsDueForRelease(now)).Take(take).ToList());
        public Task<List<Payment>> ChangedBetween(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult(Payments.ToList());
        public Task<bool> HasReceipt(string partner, string id, CancellationToken ct) => Task.FromResult(Receipts.Any(r => r.Partner == partner && r.EventId == id));
        public Task Add(WebhookReceipt r, CancellationToken ct) { Receipts.Add(r); return Task.CompletedTask; }
        public Task Add(ReconciliationRun r, CancellationToken ct) { Runs.Add(r); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeDisputes : IDisputeStore
    {
        public List<Dispute> Disputes { get; } = [];
        public Task Add(Dispute d, CancellationToken ct) { Disputes.Add(d); return Task.CompletedTask; }
        public Task<Dispute?> Get(Guid id, CancellationToken ct) => Task.FromResult(Disputes.FirstOrDefault(d => d.Id == id));
        public Task<Dispute?> OpenForOrder(Guid orderId, CancellationToken ct) => Task.FromResult(Disputes.FirstOrDefault(d => d.OrderId == orderId && d.Status == DisputeStatus.Open));
        public Task<List<Dispute>> Open(CancellationToken ct) => Task.FromResult(Disputes.Where(d => d.Status == DisputeStatus.Open).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakePartner : IPaymentPartner
    {
        public bool Down { get; set; }
        public List<(string Ref, long Cents)> Released { get; } = [];
        public List<(string Ref, long Cents)> Refunded { get; } = [];
        public int Checkouts { get; private set; }

        public Task<Result<CheckoutSession>> CreateCheckout(CheckoutRequest r, CancellationToken ct)
        {
            if (Down) return Task.FromResult(Result<CheckoutSession>.Fail("payment.partner_down", "The partner is down."));
            Checkouts++;
            return Task.FromResult(Result<CheckoutSession>.Ok(new CheckoutSession($"ref_{Checkouts}", $"https://pay/{Checkouts}")));
        }

        public Task<Result> Release(string r, long cents, CancellationToken ct)
        {
            if (Down) return Task.FromResult(Result.Fail("payment.partner_down", "The partner is down."));
            Released.Add((r, cents));
            return Task.FromResult(Result.Ok());
        }

        public Task<Result> Refund(string r, long cents, CancellationToken ct)
        {
            if (Down) return Task.FromResult(Result.Fail("payment.partner_down", "The partner is down."));
            Refunded.Add((r, cents));
            return Task.FromResult(Result.Ok());
        }

        public Task<List<PartnerTransaction>> Statement(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult(new List<PartnerTransaction>());
    }

    private sealed class FakeVerifier : IWebhookVerifier
    {
        public bool Verify(string partner, string? header, string body, DateTimeOffset now) => WebhookSignature.Verify(Secret, header, body, now);
    }

    private sealed class FakeAccess : IOrganisationAccess
    {
        public HashSet<(Guid, Guid, string)> Granted { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, o, perm)));
    }

    private sealed class FakePermissions : IPermissionChecker
    {
        public HashSet<Guid> Admins { get; } = [];
        public Task<bool> Has(Guid p, string perm, CancellationToken ct) => Task.FromResult(perm == "dispute.resolve" && Admins.Contains(p));
    }

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FakeOrders _orders = new();
    private readonly FakePayments _payments = new();
    private readonly FakeDisputes _disputes = new();
    private readonly FakePartner _partner = new();
    private readonly FakeAccess _access = new();
    private readonly FakePermissions _permissions = new();
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _supplier = Guid.NewGuid();
    private readonly Guid _supplierOrg = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _rep = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();

    public PaymentFlowTests()
    {
        _orders.Orders[_orderId] = new(_orderId, _shop, _supplier, _supplierOrg, "Fresh Foods", "AB12CD34", "confirmed", "in_app", "owing", 100_000, null);
        _access.Granted.Add((_owner, _shop, "stock.receive"));
        _access.Granted.Add((_rep, _supplierOrg, "supplier.orders"));
        _permissions.Admins.Add(_admin);
    }

    private PaymentSettlement Settlement() => new(_partner, _orders, _payments);
    private StartPaymentHandler Starter() => new(_orders, _payments, _partner, _access, _clock);
    private HandlePaymentWebhookHandler Webhooks() => new(new FakeVerifier(), _payments, _orders, _clock);
    private OrderDeliveredHandler Delivered() => new(_payments, Settlement(), _clock);
    private ReleaseDuePaymentsHandler Releaser() => new(_payments, Settlement(), _clock);
    private RaiseDisputeHandler Raiser() => new(_orders, _payments, _disputes, _access, _clock);
    private ResolveDisputeHandler Resolver() => new(_disputes, _payments, Settlement(), _orders, _permissions, _clock);

    private async Task<PaymentResponse> Started() => (await Starter().Handle(new StartPayment(_orderId, new(_owner, "https://app/back")), default)).Value!;

    private async Task<Result<Unit>> Callback(string type, string partnerRef, long cents, string id = "evt_1", DateTimeOffset? signedAt = null)
    {
        var body = $"{{\"id\":\"{id}\",\"type\":\"{type}\",\"partnerRef\":\"{partnerRef}\",\"amountCents\":{cents}}}";
        return await Webhooks().Handle(new HandlePaymentWebhook("dev", WebhookSignature.Sign(Secret, signedAt ?? _clock.GetUtcNow(), body), body), default);
    }

    private async Task<Payment> Paid()
    {
        var started = await Started();
        await Callback("payment.held", "ref_1", started.AmountCents);
        return _payments.Payments.Single();
    }

    private void SupplierDelivers(long total = 100_000)
    {
        _orders.Orders[_orderId] = _orders.Orders[_orderId] with { Status = "delivered", DeliveredAt = _clock.GetUtcNow(), TotalCents = total };
    }

    private Task Deliver(bool byShop, long total = 100_000)
    {
        SupplierDelivers(total);
        return Delivered().Handle(new OrderDelivered(_orderId, _shop, _supplier, total, byShop), default);
    }

    // ---- starting ----

    [Fact]
    public async Task Paying_in_the_app_gives_a_checkout_link_for_the_whole_order_and_the_order_still_shows_owing()
    {
        var payment = await Started();

        Assert.Equal(("pending", 100_000L, "https://pay/1"), (payment.Status, payment.AmountCents, payment.CheckoutUrl));
        Assert.Equal("owing", _orders.Orders[_orderId].PaymentStatus);
    }

    [Fact]
    public async Task Only_the_shop_can_pay_and_only_an_in_app_confirmed_order_that_is_not_already_paid()
    {
        Assert.Equal("permission.denied", (await Starter().Handle(new StartPayment(_orderId, new(_rep, null)), default)).Error!.Code);
        Assert.Equal("order.not_found", (await Starter().Handle(new StartPayment(Guid.NewGuid(), new(_owner, null)), default)).Error!.Code);

        _orders.Orders[_orderId] = _orders.Orders[_orderId] with { Status = "sent" };
        Assert.Equal("payment.order_not_payable", (await Starter().Handle(new StartPayment(_orderId, new(_owner, null)), default)).Error!.Code);

        _orders.Orders[_orderId] = _orders.Orders[_orderId] with { Status = "confirmed", PaymentMethod = "on_delivery" };
        Assert.Equal("payment.not_in_app", (await Starter().Handle(new StartPayment(_orderId, new(_owner, null)), default)).Error!.Code);

        _orders.Orders[_orderId] = _orders.Orders[_orderId] with { PaymentMethod = "in_app" };
        await Paid();
        Assert.Equal("payment.already_paid", (await Starter().Handle(new StartPayment(_orderId, new(_owner, null)), default)).Error!.Code);
    }

    [Fact]
    public async Task Starting_twice_before_paying_gives_the_same_link_and_a_partner_that_is_down_creates_nothing()
    {
        var first = await Started();
        var second = await Started();
        Assert.Equal((first.Id, 1), (second.Id, _partner.Checkouts));

        _payments.Payments.Clear();
        _partner.Down = true;
        Assert.Equal("payment.partner_down", (await Starter().Handle(new StartPayment(_orderId, new(_owner, null)), default)).Error!.Code);
        Assert.Empty(_payments.Payments);
    }

    // ---- the partner's callbacks ----

    [Fact]
    public async Task A_signed_callback_that_the_money_is_held_marks_the_order_held()
    {
        var payment = await Paid();

        Assert.Equal(PaymentStatus.Held, payment.Status);
        Assert.Equal("held", _orders.Orders[_orderId].PaymentStatus);
    }

    [Fact]
    public async Task An_unsigned_tampered_or_stale_callback_is_refused_and_changes_nothing()
    {
        await Started();
        var body = "{\"id\":\"e\",\"type\":\"payment.held\",\"partnerRef\":\"ref_1\",\"amountCents\":100000}";

        Assert.Equal("webhook.signature_invalid", (await Webhooks().Handle(new HandlePaymentWebhook("dev", null, body), default)).Error!.Code);
        Assert.Equal("webhook.signature_invalid", (await Webhooks().Handle(new HandlePaymentWebhook("dev", WebhookSignature.Sign("wrong", Start, body), body), default)).Error!.Code);
        Assert.Equal("webhook.signature_invalid", (await Webhooks().Handle(new HandlePaymentWebhook("dev", WebhookSignature.Sign(Secret, Start, body + " "), body), default)).Error!.Code);
        Assert.Equal("webhook.signature_invalid", (await Callback("payment.held", "ref_1", 100_000, signedAt: Start.AddHours(-1))).Error!.Code);
        Assert.Equal(PaymentStatus.Pending, _payments.Payments.Single().Status);
        Assert.Empty(_payments.Receipts);
    }

    [Fact]
    public async Task The_same_callback_delivered_twice_is_handled_once()
    {
        await Started();
        await Callback("payment.held", "ref_1", 100_000);
        var audit = _payments.Payments.Single().Audit.Count;

        var again = await Callback("payment.held", "ref_1", 100_000);

        Assert.True(again.IsSuccess);
        Assert.Equal((audit, 1), (_payments.Payments.Single().Audit.Count, _payments.Receipts.Count));
    }

    [Fact]
    public async Task A_callback_with_the_wrong_amount_fails_the_payment_and_the_order_stays_owing()
    {
        await Started();

        var result = await Callback("payment.held", "ref_1", 99_999);

        Assert.Equal("payment.amount_mismatch", result.Error!.Code);
        Assert.Equal(PaymentStatus.Failed, _payments.Payments.Single().Status);
        Assert.Equal("owing", _orders.Orders[_orderId].PaymentStatus);
        Assert.Single(_payments.Receipts);
    }

    [Fact]
    public async Task A_failed_payment_can_be_tried_again_with_a_new_checkout()
    {
        await Started();
        await Callback("payment.failed", "ref_1", 0);

        var retry = await Started();

        Assert.Equal(("pending", 2), (retry.Status, _payments.Payments.Count));
    }

    [Fact]
    public async Task Callbacks_for_unknown_payments_malformed_bodies_and_other_event_types_are_dealt_with_calmly()
    {
        await Started();

        Assert.Equal("payment.unknown", (await Callback("payment.held", "nope", 1)).Error!.Code);
        var junk = "not json";
        Assert.Equal("webhook.payload_invalid", (await Webhooks().Handle(new HandlePaymentWebhook("dev", WebhookSignature.Sign(Secret, Start, junk), junk), default)).Error!.Code);
        Assert.True((await Callback("payout.paid", "ref_1", 5, id: "evt_other")).IsSuccess);
        Assert.Equal(PaymentStatus.Pending, _payments.Payments.Single().Status);
    }

    // ---- release ----

    [Fact]
    public async Task The_shop_confirming_delivery_pays_the_supplier_straight_away_and_the_order_shows_paid()
    {
        await Paid();

        await Deliver(byShop: true);

        Assert.Equal(("paid", PaymentStatus.Released), (_orders.Orders[_orderId].PaymentStatus, _payments.Payments.Single().Status));
        Assert.Equal([("ref_1", 100_000L)], _partner.Released);
        Assert.Empty(_partner.Refunded);
    }

    [Fact]
    public async Task A_short_delivery_pays_for_what_arrived_and_refunds_the_rest()
    {
        await Paid();

        await Deliver(byShop: true, total: 76_000);

        Assert.Equal([("ref_1", 76_000L)], _partner.Released);
        Assert.Equal([("ref_1", 24_000L)], _partner.Refunded);
    }

    [Fact]
    public async Task When_only_the_supplier_says_delivered_the_money_waits_72_hours_then_goes()
    {
        await Paid();
        await Deliver(byShop: false);

        Assert.Equal(0, (await Releaser().Handle(new ReleaseDuePayments(), default)).Value);
        _clock.Advance(TimeSpan.FromHours(71));
        Assert.Equal(0, (await Releaser().Handle(new ReleaseDuePayments(), default)).Value);
        Assert.Equal("held", _orders.Orders[_orderId].PaymentStatus);

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, (await Releaser().Handle(new ReleaseDuePayments(), default)).Value);
        Assert.Equal(("paid", 1), (_orders.Orders[_orderId].PaymentStatus, _partner.Released.Count));
        Assert.Equal(0, (await Releaser().Handle(new ReleaseDuePayments(), default)).Value);   // not paid out twice
    }

    [Fact]
    public async Task A_partner_that_is_down_leaves_the_money_held_and_the_next_round_pays_it()
    {
        await Paid();
        await Deliver(byShop: false);
        _clock.Advance(TimeSpan.FromHours(73));
        _partner.Down = true;

        Assert.Equal(0, (await Releaser().Handle(new ReleaseDuePayments(), default)).Value);
        Assert.Equal(PaymentStatus.Held, _payments.Payments.Single().Status);

        _partner.Down = false;
        Assert.Equal(1, (await Releaser().Handle(new ReleaseDuePayments(), default)).Value);
        Assert.Equal(PaymentStatus.Released, _payments.Payments.Single().Status);
    }

    [Fact]
    public async Task Delivering_an_order_paid_on_delivery_or_not_yet_paid_does_nothing_to_payments()
    {
        await Deliver(byShop: true);
        Assert.Empty(_payments.Payments);

        _orders.Orders[_orderId] = _orders.Orders[_orderId] with { Status = "confirmed" };
        await Started();
        await Deliver(byShop: true);
        Assert.Equal(PaymentStatus.Pending, _payments.Payments.Single().Status);
        Assert.Empty(_partner.Released);
    }

    // ---- disputes ----

    [Fact]
    public async Task A_dispute_holds_the_payment_even_after_72_hours()
    {
        await Paid();
        await Deliver(byShop: false);

        var raised = await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "Two cases were missing")), default);
        _clock.Advance(TimeSpan.FromDays(5));
        var released = await Releaser().Handle(new ReleaseDuePayments(), default);

        Assert.Equal(("open", "shop"), (raised.Value!.Status, raised.Value.RaisedBy));
        Assert.Equal(0, released.Value);
        Assert.Equal(("disputed", true), (_orders.Orders[_orderId].Status, _payments.Payments.Single().Frozen));
    }

    [Fact]
    public async Task Either_side_can_dispute_but_not_outsiders_and_not_twice_and_only_a_delivered_order_inside_14_days()
    {
        Assert.Equal("dispute.order_not_disputable", (await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "Not here yet")), default)).Error!.Code);
        await Paid();
        await Deliver(byShop: false);

        Assert.Equal("permission.denied", (await Raiser().Handle(new RaiseDispute(_orderId, new(Guid.NewGuid(), "I am a stranger")), default)).Error!.Code);
        Assert.Equal("dispute.reason_required", (await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "")), default)).Error!.Code);

        var bySupplier = await Raiser().Handle(new RaiseDispute(_orderId, new(_rep, "Shop refuses to accept the goods")), default);
        Assert.Equal("supplier", bySupplier.Value!.RaisedBy);
        Assert.Equal("dispute.already_open", (await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "Another one")), default)).Error!.Code);
    }

    [Fact]
    public async Task The_14_days_to_dispute_run_from_delivery()
    {
        await Deliver(byShop: false);
        _clock.Advance(TimeSpan.FromDays(15));

        Assert.Equal("dispute.window_closed", (await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "Too late really")), default)).Error!.Code);
    }

    [Fact]
    public async Task Resolving_for_the_supplier_pays_them_and_the_order_goes_back_to_delivered_and_paid()
    {
        await Paid();
        await Deliver(byShop: false);
        var dispute = (await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "Two cases were missing")), default)).Value!;

        var resolved = await Resolver().Handle(new ResolveDispute(dispute.Id, new(_admin, "pay_supplier", "Delivery note was signed")), default);

        Assert.Equal(("resolved", "pay_supplier"), (resolved.Value!.Status, resolved.Value.Outcome));
        Assert.Equal(("delivered", "paid"), (_orders.Orders[_orderId].Status, _orders.Orders[_orderId].PaymentStatus));
        Assert.Equal([("ref_1", 100_000L)], _partner.Released);
    }

    [Fact]
    public async Task Resolving_for_the_shop_refunds_everything()
    {
        await Paid();
        await Deliver(byShop: false);
        var dispute = (await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "Goods were spoiled")), default)).Value!;

        await Resolver().Handle(new ResolveDispute(dispute.Id, new(_admin, "refund_shop", "Photos show spoilage")), default);

        Assert.Equal("refunded", _orders.Orders[_orderId].PaymentStatus);
        Assert.Equal([("ref_1", 100_000L)], _partner.Refunded);
        Assert.Empty(_partner.Released);
    }

    [Fact]
    public async Task Only_an_admin_resolves_and_a_partner_that_is_down_leaves_the_dispute_open_to_try_again()
    {
        await Paid();
        await Deliver(byShop: false);
        var dispute = (await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "Goods were spoiled")), default)).Value!;

        Assert.Equal("permission.denied", (await Resolver().Handle(new ResolveDispute(dispute.Id, new(_owner, "refund_shop", "I say so")), default)).Error!.Code);
        Assert.Equal("dispute.outcome_invalid", (await Resolver().Handle(new ResolveDispute(dispute.Id, new(_admin, "split", "Half each")), default)).Error!.Code);
        Assert.Equal("dispute.not_found", (await Resolver().Handle(new ResolveDispute(Guid.NewGuid(), new(_admin, "refund_shop", "Nothing")), default)).Error!.Code);

        _partner.Down = true;
        Assert.Equal("payment.partner_down", (await Resolver().Handle(new ResolveDispute(dispute.Id, new(_admin, "refund_shop", "Photos show spoilage")), default)).Error!.Code);
        _partner.Down = false;
        Assert.Equal(PaymentStatus.Held, _payments.Payments.Single().Status);
        Assert.True((await Resolver().Handle(new ResolveDispute(dispute.Id, new(_admin, "refund_shop", "Photos show spoilage")), default)).IsSuccess);
    }

    [Fact]
    public async Task A_dispute_on_an_order_paid_on_delivery_just_flags_the_order_and_resolves_without_moving_money()
    {
        _orders.Orders[_orderId] = _orders.Orders[_orderId] with { PaymentMethod = "on_delivery" };
        SupplierDelivers();
        var dispute = (await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "Short delivery of Coke")), default)).Value!;

        await Resolver().Handle(new ResolveDispute(dispute.Id, new(_admin, "pay_supplier", "Count was right")), default);

        Assert.Equal("delivered", _orders.Orders[_orderId].Status);
        Assert.Empty(_partner.Released);
        Assert.Empty(_partner.Refunded);
    }

    [Fact]
    public async Task Open_disputes_are_for_admins_only()
    {
        await Deliver(byShop: false);
        await Raiser().Handle(new RaiseDispute(_orderId, new(_owner, "Short delivery of Coke")), default);
        var list = new ListOpenDisputesHandler(_disputes, _permissions);

        Assert.Single((await list.Handle(new ListOpenDisputes(_admin), default)).Value!);
        Assert.Equal("permission.denied", (await list.Handle(new ListOpenDisputes(_owner), default)).Error!.Code);
    }

    // ---- reading ----

    [Fact]
    public async Task Both_sides_can_read_the_payment_with_its_audit_trail_and_strangers_cannot()
    {
        await Paid();
        var reader = new GetOrderPaymentHandler(_orders, _payments, _access);

        var byShop = await reader.Handle(new GetOrderPayment(_orderId, _owner), default);
        var bySupplier = await reader.Handle(new GetOrderPayment(_orderId, _rep), default);

        Assert.Equal(["pending", "held"], byShop.Value!.Audit.Select(a => a.To));
        Assert.True(bySupplier.IsSuccess);
        Assert.Equal("permission.denied", (await reader.Handle(new GetOrderPayment(_orderId, Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("payment.not_found", (await new GetOrderPaymentHandler(_orders, new FakePayments(), _access).Handle(new GetOrderPayment(_orderId, _owner), default)).Error!.Code);
    }

    // ---- reconciliation ----

    [Fact]
    public async Task Reconciliation_matches_to_the_cent_and_lists_every_difference()
    {
        var p = await Paid();
        var day = DateOnly.FromDateTime(Start.UtcDateTime);

        var good = Reconciler.Compare(day, [p], [new("ref_1", 100_000, "held", 0, 0)]);
        var amount = Reconciler.Compare(day, [p], [new("ref_1", 100_001, "held", 0, 0)]);
        var state = Reconciler.Compare(day, [p], [new("ref_1", 100_000, "pending", 0, 0)]);
        var missing = Reconciler.Compare(day, [p], []);
        var unknown = Reconciler.Compare(day, [p], [new("ref_1", 100_000, "held", 0, 0), new("ref_9", 500, "held", 0, 0)]);

        Assert.Equal((1, 0), (good.Matched, good.Mismatches.Count));
        Assert.Equal("amount", amount.Mismatches.Single().Kind);
        Assert.Equal("state", state.Mismatches.Single().Kind);
        Assert.Equal("missing_at_partner", missing.Mismatches.Single().Kind);
        Assert.Equal(("ref_9", "unknown_to_us"), (unknown.Mismatches.Single().PartnerRef, unknown.Mismatches.Single().Kind));
    }

    [Fact]
    public async Task A_settled_payment_must_match_the_partners_released_and_refunded_amounts()
    {
        await Paid();
        await Deliver(byShop: true, total: 76_000);
        var p = _payments.Payments.Single();
        var day = DateOnly.FromDateTime(Start.UtcDateTime);

        Assert.Equal(1, Reconciler.Compare(day, [p], [new("ref_1", 100_000, "released", 76_000, 24_000)]).Matched);
        Assert.Equal("settlement", Reconciler.Compare(day, [p], [new("ref_1", 100_000, "released", 100_000, 0)]).Mismatches.Single().Kind);
    }

    [Fact]
    public async Task Running_reconciliation_needs_an_admin_and_keeps_a_record()
    {
        await Paid();
        var handler = new ReconcilePaymentsHandler(_payments, _partner, _permissions, _clock);
        var day = DateOnly.FromDateTime(Start.UtcDateTime);

        Assert.Equal("permission.denied", (await handler.Handle(new ReconcilePayments(day, _owner), default)).Error!.Code);
        var run = await handler.Handle(new ReconcilePayments(day, _admin), default);

        Assert.Equal("missing_at_partner", run.Value!.Mismatches.Single().Kind);   // the fake partner's statement is empty
        Assert.Single(_payments.Runs);
    }
}
