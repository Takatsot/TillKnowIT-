using Billing.Contracts;
using Finance.Application;
using Finance.Contracts;
using Finance.Domain;
using Finance.Infrastructure;
using Identity.Contracts;
using Marketplace.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Contracts;
using TillKnowIT.Security;
using TillKnowIT.SharedKernel;

namespace Finance.Application.Tests;

public class FinanceTests
{
    private sealed class Store : IFinanceStore
    {
        public List<CreditConsent> ConsentRows { get; } = [];
        public List<DataShareRecord> ShareRows { get; } = [];
        public List<CreditApplication> Apps { get; } = [];
        public List<CreditDraw> DrawRows { get; } = [];
        public List<CreditRepayment> Repayments { get; } = [];
        public void Add(CreditConsent c) => ConsentRows.Add(c);
        public Task<CreditConsent?> Consent(Guid shopId, Guid id, CancellationToken ct) => Task.FromResult(ConsentRows.FirstOrDefault(c => c.ShopId == shopId && c.Id == id));
        public Task<CreditConsent?> ActiveConsent(Guid shopId, string code, CancellationToken ct) => Task.FromResult(ConsentRows.FirstOrDefault(c => c.ShopId == shopId && c.PartnerCode == code && c.IsActive));
        public Task<List<CreditConsent>> Consents(Guid shopId, CancellationToken ct) => Task.FromResult(ConsentRows.Where(c => c.ShopId == shopId).ToList());
        public void Add(DataShareRecord r) => ShareRows.Add(r);
        public Task<List<DataShareRecord>> Shares(Guid shopId, int take, CancellationToken ct) => Task.FromResult(ShareRows.Where(s => s.ShopId == shopId).ToList());
        public void Add(CreditApplication a) => Apps.Add(a);
        public Task<CreditApplication?> Application(Guid id, CancellationToken ct) => Task.FromResult(Apps.FirstOrDefault(a => a.Id == id));
        public Task<List<CreditApplication>> Applications(Guid shopId, CancellationToken ct) => Task.FromResult(Apps.Where(a => a.ShopId == shopId).ToList());
        public void Add(CreditDraw d) => DrawRows.Add(d);
        public Task<CreditDraw?> DrawForOrder(Guid orderId, CancellationToken ct) => Task.FromResult(DrawRows.FirstOrDefault(d => d.OrderId == orderId));
        public Task<CreditDraw?> DrawByReference(string partner, string reference, CancellationToken ct) => Task.FromResult(DrawRows.FirstOrDefault(d => d.PartnerReference == reference));
        public Task<List<CreditDraw>> Draws(Guid shopId, CancellationToken ct) => Task.FromResult(DrawRows.Where(d => d.ShopId == shopId).ToList());
        public Task<List<CreditDraw>> DrawsOf(Guid appId, CancellationToken ct) => Task.FromResult(DrawRows.Where(d => d.ApplicationId == appId).ToList());
        public void Add(CreditRepayment r) => Repayments.Add(r);
        public Task<bool> RepaymentExists(string reference, CancellationToken ct) => Task.FromResult(Repayments.Any(r => r.Reference == reference));
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Access : IOrganisationAccess
    {
        public HashSet<Guid> Owners { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Owners.Contains(p) && perm == "shop.settings");
    }

    private sealed class Plans : IEntitlements
    {
        public bool PayLater { get; set; } = true;
        public Task<bool> Has(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult(PayLater && feature == PlanFeatures.PayLater);
        public Task<long?> LimitFor(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult<long?>(null);
    }

    private sealed class FakeBranches : IBranchDirectory
    {
        public Task<Guid> HeadOf(Guid shopId, CancellationToken ct) => Task.FromResult(shopId);
        public Task<List<BranchResponse>> Group(Guid shopId, CancellationToken ct) => Task.FromResult(new List<BranchResponse>());
    }

    private sealed class History : ISalesHistory
    {
        public long PerMonth { get; set; } = 400_000;
        public int Asked { get; private set; }
        public Task<List<MonthlySales>> Months(Guid shopId, int months, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(Enumerable.Range(1, months).Select(i => new MonthlySales($"2026-{i:D2}", PerMonth, 100)).ToList());
        }
    }

    private sealed class Orders : IOrderPayments
    {
        public Dictionary<Guid, OrderPaymentInfo> Rows { get; } = [];
        public List<(Guid, string)> Statuses { get; } = [];
        public Task<OrderPaymentInfo?> Get(Guid orderId, CancellationToken ct) => Task.FromResult(Rows.GetValueOrDefault(orderId));
        public Task<Result> SetPaymentStatus(Guid orderId, string status, CancellationToken ct) { Statuses.Add((orderId, status)); return Task.FromResult(Result.Ok()); }
        public Task<Result> SetDisputed(Guid orderId, bool disputed, CancellationToken ct) => Task.FromResult(Result.Ok());
    }

    private const string Secret = "s3cret";
    private readonly Store _store = new();
    private readonly Access _access = new();
    private readonly Plans _plans = new();
    private readonly History _history = new();
    private readonly Orders _orders = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly DevFinancePartner _partner;

    public FinanceTests()
    {
        _partner = new DevFinancePartner(Secret, _clock);
        _access.Owners.Add(_owner);
    }

    private FinanceHandlers Handlers => new(_store, [_partner], _access, _plans, new FakeBranches(), _history, _orders, _clock);

    private async Task<ConsentResponse> Consent(int months = 12) => (await Handlers.Handle(new GrantConsent(_shop, new(_owner, "dev", months)), default)).Value!;

    private async Task<ApplicationResponse> Approved(long requested = 500_000)
    {
        var consent = await Consent();
        return (await Handlers.Handle(new ApplyForCredit(_shop, new(_owner, Guid.NewGuid(), consent.Id, requested)), default)).Value!;
    }

    private Guid Order(long total, string status = "owing")
    {
        var id = Guid.NewGuid();
        _orders.Rows[id] = new(id, _shop, Guid.NewGuid(), Guid.NewGuid(), "Cash and Carry", "ORD-1", "delivered", "credit", status, total, null);
        return id;
    }

    // ---- consent ----

    [Fact]
    public async Task The_owner_agrees_to_share_with_a_named_partner_and_agreeing_twice_is_one_consent()
    {
        var one = await Consent();
        var two = await Consent();

        Assert.Equal(one.Id, two.Id);
        Assert.Equal(("dev", 12, true), (one.PartnerCode, one.Months, one.Active));
        Assert.Single(_store.ConsentRows);
    }

    [Fact]
    public async Task Consent_needs_the_owner_the_plan_a_known_partner_and_a_sensible_period()
    {
        Assert.Equal("permission.denied", (await Handlers.Handle(new GrantConsent(_shop, new(Guid.NewGuid(), "dev", 12)), default)).Error!.Code);
        Assert.Equal("consent.partner_unknown", (await Handlers.Handle(new GrantConsent(_shop, new(_owner, "bank", 12)), default)).Error!.Code);
        Assert.Equal("consent.months_invalid", (await Handlers.Handle(new GrantConsent(_shop, new(_owner, "dev", 36)), default)).Error!.Code);
        _plans.PayLater = false;
        Assert.Equal("plan.feature_not_included", (await Handlers.Handle(new GrantConsent(_shop, new(_owner, "dev", 12)), default)).Error!.Code);
    }

    [Fact]
    public async Task Withdrawing_stops_all_further_sharing_at_once()
    {
        var consent = await Consent();
        await Handlers.Handle(new ApplyForCredit(_shop, new(_owner, Guid.NewGuid(), consent.Id, 100_000)), default);
        Assert.Equal(1, _history.Asked);

        var withdrawn = (await Handlers.Handle(new WithdrawConsent(_shop, consent.Id, _owner), default)).Value!;
        var again = await Handlers.Handle(new ApplyForCredit(_shop, new(_owner, Guid.NewGuid(), consent.Id, 100_000)), default);

        Assert.False(withdrawn.Active);
        Assert.Equal("consent.withdrawn", again.Error!.Code);
        Assert.Equal(1, _history.Asked);   // nothing more was read, let alone sent
        Assert.Equal("consent.already_withdrawn", (await Handlers.Handle(new WithdrawConsent(_shop, consent.Id, _owner), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new WithdrawConsent(_shop, consent.Id, Guid.NewGuid()), default)).Error!.Code);
    }

    // ---- applying ----

    [Fact]
    public async Task An_application_shares_only_the_agreed_months_records_it_and_stores_the_partners_decision()
    {
        var consent = await Consent(months: 6);

        var app = (await Handlers.Handle(new ApplyForCredit(_shop, new(_owner, Guid.NewGuid(), consent.Id, 500_000)), default)).Value!;

        Assert.Equal(("approved", 500_000L, 30), (app.Status, app.ApprovedCents, app.TermDays));
        var share = Assert.Single(_store.ShareRows);
        Assert.Equal((6, 6 * 400_000L), (share.Months, share.TotalSalesCents));
    }

    [Fact]
    public async Task The_partner_can_decline_and_the_limit_is_what_it_decides_not_what_was_asked()
    {
        _history.PerMonth = 1_000;
        var declined = await Approved();
        Assert.Equal(("declined", 0L), (declined.Status, declined.ApprovedCents));
        Assert.Equal("Not enough sales history yet.", declined.Reason);

        _history.PerMonth = 100_000;
        var smaller = await Approved(requested: 10_000_000);
        Assert.Equal(600_000, smaller.ApprovedCents);   // half of 12 months of R1000
    }

    [Fact]
    public async Task Applying_twice_with_the_same_id_is_one_application_and_one_share()
    {
        var consent = await Consent();
        var id = Guid.NewGuid();

        await Handlers.Handle(new ApplyForCredit(_shop, new(_owner, id, consent.Id, 100_000)), default);
        await Handlers.Handle(new ApplyForCredit(_shop, new(_owner, id, consent.Id, 100_000)), default);

        Assert.Single(_store.Apps);
        Assert.Single(_store.ShareRows);
    }

    [Fact]
    public async Task An_application_needs_the_owner_a_real_consent_and_a_sensible_amount()
    {
        var consent = await Consent();

        Assert.Equal("permission.denied", (await Handlers.Handle(new ApplyForCredit(_shop, new(Guid.NewGuid(), Guid.NewGuid(), consent.Id, 100_000)), default)).Error!.Code);
        Assert.Equal("consent.not_found", (await Handlers.Handle(new ApplyForCredit(_shop, new(_owner, Guid.NewGuid(), Guid.NewGuid(), 100_000)), default)).Error!.Code);
        Assert.Equal("credit.amount_invalid", (await Handlers.Handle(new ApplyForCredit(_shop, new(_owner, Guid.NewGuid(), consent.Id, 5_000)), default)).Error!.Code);
    }

    // ---- paying a supplier with terms ----

    [Fact]
    public async Task An_order_paid_with_terms_is_paid_by_the_partner_marked_paid_and_owed_back_with_the_fee()
    {
        var app = await Approved();
        var order = Order(200_000);

        var draw = (await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, app.Id, order)), default)).Value!;

        Assert.Equal((200_000L, 210_000L, 0L), (draw.PrincipalCents, draw.RepayCents, draw.RepaidCents));
        Assert.Equal(new DateOnly(2026, 11, 25), draw.DueOn);
        Assert.Equal([(order, "paid")], _orders.Statuses);
        var summary = (await Handlers.Handle(new GetCreditSummary(_shop, _owner), default)).Value!;
        Assert.Equal(300_000, summary.Applications.Single().AvailableCents);
    }

    [Fact]
    public async Task Paying_the_same_order_twice_is_one_draw_and_credit_cannot_go_over_the_limit()
    {
        var app = await Approved(300_000);
        var big = Order(200_000);
        await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, app.Id, big)), default);
        await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, app.Id, big)), default);
        Assert.Single(_store.DrawRows);

        var tooMuch = await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, app.Id, Order(150_000))), default);
        Assert.Equal("credit.limit_exceeded", tooMuch.Error!.Code);
        Assert.True((await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, app.Id, Order(100_000))), default)).IsSuccess);
    }

    [Fact]
    public async Task Only_an_approved_application_can_pay_an_owing_order_of_the_shop()
    {
        _history.PerMonth = 1_000;
        var declined = await Approved();
        Assert.Equal("credit.not_approved", (await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, declined.Id, Order(1_000))), default)).Error!.Code);

        _history.PerMonth = 400_000;
        var app = await Approved();
        Assert.Equal("order.not_owing", (await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, app.Id, Order(1_000, "paid"))), default)).Error!.Code);
        Assert.Equal("order.not_found", (await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, app.Id, Guid.NewGuid())), default)).Error!.Code);
        Assert.Equal("application.not_found", (await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, Guid.NewGuid(), Order(1_000))), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new DrawOnCredit(_shop, new(Guid.NewGuid(), app.Id, Order(1_000))), default)).Error!.Code);
    }

    // ---- repayments ----

    private (string Body, string Signature) Callback(string reference, string draw, long cents)
    {
        var body = $"{{\"reference\":\"{reference}\",\"drawReference\":\"{draw}\",\"amountCents\":{cents},\"paidAt\":\"2026-11-20T09:00:00+00:00\"}}";
        return (body, WebhookSignature.Sign(Secret, _clock.GetUtcNow(), body));
    }

    [Fact]
    public async Task A_signed_repayment_is_tracked_credit_comes_back_and_a_repeat_counts_once()
    {
        var app = await Approved(300_000);
        var order = Order(200_000);
        var draw = (await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, app.Id, order)), default)).Value!;
        var (body, sig) = Callback("rep-1", $"dev-draw-{order:N}", 105_000);

        Assert.True((await Handlers.Handle(new HandleFinanceCallback("dev", sig, body), default)).IsSuccess);
        Assert.True((await Handlers.Handle(new HandleFinanceCallback("dev", sig, body), default)).IsSuccess);

        var summary = (await Handlers.Handle(new GetCreditSummary(_shop, _owner), default)).Value!;
        Assert.Single(_store.Repayments);
        Assert.Equal((105_000L, "disbursed"), (summary.Draws.Single().RepaidCents, summary.Draws.Single().Status));
        Assert.Equal(200_000, summary.Applications.Single().AvailableCents);   // half of the R2 100 debt is paid, so R1 000 of the R2 000 principal is back
        var (body2, sig2) = Callback("rep-2", $"dev-draw-{order:N}", 105_000);
        await Handlers.Handle(new HandleFinanceCallback("dev", sig2, body2), default);
        Assert.Equal("repaid", (await Handlers.Handle(new GetCreditSummary(_shop, _owner), default)).Value!.Draws.Single().Status);
        _ = draw;
    }

    [Fact]
    public async Task A_callback_with_a_bad_signature_a_stale_time_or_an_unknown_draw_is_refused()
    {
        var app = await Approved();
        var order = Order(100_000);
        await Handlers.Handle(new DrawOnCredit(_shop, new(_owner, app.Id, order)), default);
        var (body, _) = Callback("rep-1", $"dev-draw-{order:N}", 1_000);

        Assert.Equal("webhook.signature_invalid", (await Handlers.Handle(new HandleFinanceCallback("dev", "t=1,v1=00", body), default)).Error!.Code);
        Assert.Equal("webhook.signature_invalid", (await Handlers.Handle(new HandleFinanceCallback("dev", WebhookSignature.Sign(Secret, _clock.GetUtcNow().AddHours(-1), body), body), default)).Error!.Code);
        var (unknown, sig) = Callback("rep-9", "dev-draw-nope", 1_000);
        Assert.Equal("draw.not_found", (await Handlers.Handle(new HandleFinanceCallback("dev", sig, unknown), default)).Error!.Code);
        Assert.Equal("partner.not_found", (await Handlers.Handle(new HandleFinanceCallback("other", sig, unknown), default)).Error!.Code);
        Assert.Equal("webhook.body_invalid", (await Handlers.Handle(new HandleFinanceCallback("dev", WebhookSignature.Sign(Secret, _clock.GetUtcNow(), "nope"), "nope"), default)).Error!.Code);
        Assert.Empty(_store.Repayments);
    }

    [Fact]
    public async Task The_summary_lists_exactly_what_was_shared_and_when()
    {
        await Approved();

        var summary = (await Handlers.Handle(new GetCreditSummary(_shop, _owner), default)).Value!;

        Assert.Equal(("dev", 12), (summary.Shared.Single().PartnerCode, summary.Shared.Single().Months));
        Assert.Equal("permission.denied", (await Handlers.Handle(new GetCreditSummary(_shop, Guid.NewGuid()), default)).Error!.Code);
    }
}
