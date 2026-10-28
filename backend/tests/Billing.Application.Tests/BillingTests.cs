using Billing.Application;
using Billing.Contracts;
using Billing.Domain;
using Identity.Contracts;
using Messaging.Contracts;
using Microsoft.Extensions.Time.Testing;
using TillKnowIT.SharedKernel;

namespace Billing.Application.Tests;

public class BillingMathTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Vat_is_added_once_and_rounds_half_up()
    {
        Assert.Equal(2_985, BillingMath.Vat(19_900, 15));
        Assert.Equal(0, BillingMath.Vat(3, 15));   // 0.45 rounds down
        Assert.Equal(1, BillingMath.Vat(4, 15));   // 0.6 rounds up
        Assert.Equal(0, BillingMath.Vat(0, 15));
    }

    [Fact]
    public void An_upgrade_pays_the_new_plan_for_the_days_left_less_what_is_left_of_the_old_one()
    {
        var midway = Start.AddDays(15);

        Assert.Equal(5_000, BillingMath.UpgradeCharge(10_000, 20_000, midway, Start, End));
        Assert.Equal(10_000, BillingMath.UpgradeCharge(10_000, 20_000, Start, Start, End));
        Assert.Equal(0, BillingMath.UpgradeCharge(20_000, 10_000, midway, Start, End));   // a downgrade is never refunded or charged
        Assert.Equal(0, BillingMath.UpgradeCharge(10_000, 20_000, End, Start, End));
    }

    [Fact]
    public void Usage_is_billed_only_above_what_the_plan_includes()
    {
        Assert.Equal((0L, 0L), BillingMath.Overage(40, 50, 40));
        Assert.Equal((10L, 400L), BillingMath.Overage(60, 50, 40));
        Assert.Equal((5L, 200L), BillingMath.Overage(5, null, 40));
    }
}

public class BillingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Plan Pro = Plan.Create("shop_pro", "shop", "Pro", 19_900, Now).Value!;
    private static readonly Plan Free = Plan.Create("shop_free", "shop", "Free", 0, Now).Value!;

    [Fact]
    public void A_plan_needs_a_clean_code_a_known_audience_a_name_and_a_price_that_is_not_negative()
    {
        Assert.Equal("plan.code_invalid", Plan.Create("Bad Code", "shop", "X", 0, Now).Error!.Code);
        Assert.Equal("plan.audience_invalid", Plan.Create("x", "bank", "X", 0, Now).Error!.Code);
        Assert.Equal("plan.name_invalid", Plan.Create("x", "shop", " ", 0, Now).Error!.Code);
        Assert.Equal("plan.price_invalid", Plan.Create("x", "shop", "X", -1, Now).Error!.Code);
    }

    [Fact]
    public void A_failed_payment_makes_it_past_due_and_reminders_come_on_the_days_in_settings_then_the_extras_stop_after_the_grace_period()
    {
        var s = Subscription.Start(Guid.NewGuid(), Pro, Guid.NewGuid(), Now);
        var settings = new BillingSettings();
        s.PaymentFailed(Now);

        Assert.Equal(1, s.Dunning(Now, settings).Reminder);
        Assert.Null(s.Dunning(Now.AddDays(1), settings).Reminder);      // nothing due yet
        Assert.Equal(2, s.Dunning(Now.AddDays(3), settings).Reminder);
        Assert.Equal(3, s.Dunning(Now.AddDays(6), settings).Reminder);
        var end = s.Dunning(Now.AddDays(7), settings);
        Assert.Equal((null, true), (end.Reminder, end.NowLimited));
        Assert.Equal(SubscriptionStatus.Limited, s.Status);
        Assert.False(s.GivesPaidFeatures);

        s.PaymentReceived();
        Assert.Equal((SubscriptionStatus.Active, true), (s.Status, s.GivesPaidFeatures));
    }

    [Fact]
    public void A_downgrade_waits_for_the_period_end_and_cancelling_ends_it_there_and_a_change_undoes_a_cancel()
    {
        var s = Subscription.Start(Guid.NewGuid(), Pro, Guid.NewGuid(), Now);

        Assert.True(s.Change(Free, atPeriodEnd: true).IsSuccess);
        Assert.Equal((Pro.Id, Free.Id), (s.PlanId, s.PendingPlanId));
        Assert.True(s.Renew(Free));
        Assert.Equal((Free.Id, null), (s.PlanId, s.PendingPlanId));

        Assert.True(s.Cancel().IsSuccess);
        Assert.True(s.Change(Pro, atPeriodEnd: false).IsSuccess);
        Assert.False(s.CancelAtPeriodEnd);
        s.Cancel();
        Assert.False(s.Renew(null));
        Assert.Equal("subscription.cancelled", s.Cancel().Error!.Code);
    }

    [Fact]
    public void An_invoice_adds_vat_to_its_lines_once()
    {
        var lines = new List<InvoiceLine> { new("Pro", null, 1, 19_900, Now), new("messages", "messages", 10, 40, Now) };

        var i = Invoice.Issue("INV-1", "r", Guid.NewGuid(), null, lines, new BillingSettings(), Now);

        Assert.Equal((20_300L, 3_045L, 23_345L), (i.SubtotalCents, i.VatCents, i.TotalCents));
        Assert.All(lines, l => Assert.Equal(i.Id, l.InvoiceId));
    }

    [Fact]
    public void A_campaign_never_spends_more_than_its_budget()
    {
        var today = new DateOnly(2026, 10, 1);
        var c = SponsoredCampaign.Create(Guid.NewGuid(), "Cold drinks", null, 1_000, 100, ["Soweto", "soweto", "Alex"], today, today.AddDays(7), today, Now).Value!;

        Assert.Equal(["alex", "soweto"], c.AreaList);
        Assert.True(c.RunsOn(today, "Soweto"));
        Assert.False(c.RunsOn(today, "Durban"));
        Assert.Equal(6, c.Show(6).Value);
        Assert.Equal(4, c.Show(100).Value);          // only four more are affordable
        Assert.Equal(1_000, c.SpentCents);
        Assert.Equal(CampaignStatus.Ended, c.Status);
        Assert.Equal("campaign.not_running", c.Show(1).Error!.Code);
        Assert.False(c.RunsOn(today, "Soweto"));
    }

    [Fact]
    public void A_campaign_needs_areas_a_budget_and_sensible_dates()
    {
        var today = new DateOnly(2026, 10, 1);
        Result<SponsoredCampaign> Make(long budget = 1_000, long price = 100, string[]? areas = null, DateOnly? ends = null) =>
            SponsoredCampaign.Create(Guid.NewGuid(), "C", null, budget, price, areas ?? ["a"], today, ends ?? today.AddDays(1), today, Now);

        Assert.Equal("campaign.budget_invalid", Make(budget: 0).Error!.Code);
        Assert.Equal("campaign.price_invalid", Make(price: 2_000).Error!.Code);
        Assert.Equal("campaign.areas_required", Make(areas: []).Error!.Code);
        Assert.Equal("campaign.dates_invalid", Make(ends: today.AddDays(-1)).Error!.Code);
    }
}

public class BillingFlowTests
{
    private sealed class Store : IBillingStore
    {
        public List<Plan> PlanRows { get; } = [];
        public List<PlanFeature> FeatureRows { get; } = [];
        public List<Subscription> Subs { get; } = [];
        public List<Invoice> Invoices { get; } = [];
        public List<InvoiceLine> Lines { get; } = [];
        public List<UsageRecord> UsageRows { get; } = [];
        public List<SponsoredCampaign> Campaigns { get; } = [];

        public void Add(Plan plan) => PlanRows.Add(plan);
        public Task<Plan?> PlanByCode(string code, CancellationToken ct) => Task.FromResult(PlanRows.FirstOrDefault(p => p.Code == code));
        public Task<Plan?> PlanById(Guid id, CancellationToken ct) => Task.FromResult(PlanRows.FirstOrDefault(p => p.Id == id));
        public Task<List<Plan>> Plans(string? audience, bool activeOnly, CancellationToken ct) => Task.FromResult(PlanRows.Where(p => (audience == null || p.Audience == audience) && (!activeOnly || p.IsActive)).ToList());
        public Task<Plan?> FreePlan(string audience, CancellationToken ct) => Task.FromResult(PlanRows.FirstOrDefault(p => p.Audience == audience && p.PriceCents == 0));
        public Task<Dictionary<Guid, List<PlanFeature>>> FeaturesOf(IReadOnlyCollection<Guid> planIds, CancellationToken ct) =>
            Task.FromResult(FeatureRows.Where(f => planIds.Contains(f.PlanId)).GroupBy(f => f.PlanId).ToDictionary(g => g.Key, g => g.ToList()));
        public Task ReplaceFeatures(Guid planId, IReadOnlyList<PlanFeatureDto> features, DateTimeOffset now, CancellationToken ct)
        {
            FeatureRows.RemoveAll(f => f.PlanId == planId);
            FeatureRows.AddRange(features.Select(f => new PlanFeature(planId, f.Feature, f.Limit, now)));
            return Task.CompletedTask;
        }
        public void Add(Subscription s) => Subs.Add(s);
        public Task<Subscription?> SubscriptionOf(Guid organisationId, CancellationToken ct) => Task.FromResult(Subs.FirstOrDefault(s => s.OrganisationId == organisationId));
        public Task<Subscription?> SubscriptionById(Guid id, CancellationToken ct) => Task.FromResult(Subs.FirstOrDefault(s => s.Id == id));
        public Task<List<Subscription>> DueSubscriptions(DateTimeOffset now, int take, CancellationToken ct) => Task.FromResult(Subs.Where(s => s.IsDue(now)).Take(take).ToList());
        public Task<List<Subscription>> PastDue(int take, CancellationToken ct) => Task.FromResult(Subs.Where(s => s.Status == SubscriptionStatus.PastDue).Take(take).ToList());
        public void Add(Invoice invoice, IReadOnlyList<InvoiceLine> lines) { Invoices.Add(invoice); Lines.AddRange(lines); }
        public Task<Invoice?> InvoiceByReference(string reference, CancellationToken ct) => Task.FromResult(Invoices.FirstOrDefault(i => i.Reference == reference));
        public Task<Invoice?> InvoiceById(Guid id, CancellationToken ct) => Task.FromResult(Invoices.FirstOrDefault(i => i.Id == id));
        public Task<List<Invoice>> InvoicesOf(Guid org, int take, CancellationToken ct) => Task.FromResult(Invoices.Where(i => i.OrganisationId == org).OrderByDescending(i => i.CreatedAt).Take(take).ToList());
        public Task<Invoice?> LatestFailed(Guid org, CancellationToken ct) => Task.FromResult(Invoices.LastOrDefault(i => i.OrganisationId == org && i.Status == InvoiceStatus.Failed));
        public Task<List<InvoiceLine>> LinesOf(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult(Lines.Where(l => ids.Contains(l.InvoiceId)).ToList());
        public Task<int> InvoiceCount(CancellationToken ct) => Task.FromResult(Invoices.Count);
        public void Add(UsageRecord usage) => UsageRows.Add(usage);
        public Task<UsageRecord?> UsageByReference(Guid org, string reference, CancellationToken ct) => Task.FromResult(UsageRows.FirstOrDefault(u => u.OrganisationId == org && u.Reference == reference));
        public Task<long> UsageTotal(Guid org, string metric, DateTimeOffset from, CancellationToken ct) => Task.FromResult(UsageRows.Where(u => u.OrganisationId == org && u.Metric == metric && u.OccurredAt >= from).Sum(u => u.Quantity));
        public Task<List<UsageRecord>> UnbilledUsage(Guid org, DateTimeOffset before, CancellationToken ct) => Task.FromResult(UsageRows.Where(u => u.OrganisationId == org && u.InvoiceId == null && u.OccurredAt < before).ToList());
        public Task<List<Guid>> OrganisationsWithUnbilledPricedUsage(DateTimeOffset before, int take, CancellationToken ct) =>
            Task.FromResult(UsageRows.Where(u => u.InvoiceId == null && u.UnitCents != null && u.OccurredAt < before).Select(u => u.OrganisationId).Distinct().ToList());
        public void Add(SponsoredCampaign c) => Campaigns.Add(c);
        public Task<SponsoredCampaign?> Campaign(Guid id, CancellationToken ct) => Task.FromResult(Campaigns.FirstOrDefault(c => c.Id == id));
        public Task<List<SponsoredCampaign>> CampaignsOf(Guid org, CancellationToken ct) => Task.FromResult(Campaigns.Where(c => c.SupplierOrganisationId == org).ToList());
        public Task<List<SponsoredCampaign>> RunningOn(DateOnly day, CancellationToken ct) => Task.FromResult(Campaigns.Where(c => c.Status == CampaignStatus.Active).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Charger : IBillingCharger
    {
        public bool Decline { get; set; }
        public List<(string Number, long Cents)> Charged { get; } = [];
        public Task<Result> Charge(Guid org, string number, long total, string reference, CancellationToken ct)
        {
            if (Decline) return Task.FromResult(Result.Fail("payment.declined", "Declined."));
            Charged.Add((number, total));
            return Task.FromResult(Result.Ok());
        }
    }

    private sealed class Access : IOrganisationAccess, IPermissionChecker, IPersonDirectory
    {
        public HashSet<(Guid, string)> Granted { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, perm)));
        public Task<bool> Has(Guid p, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, perm)));
        public Task<PersonContact?> Get(Guid id, CancellationToken ct) => Task.FromResult<PersonContact?>(new(id, "Thandi", "+27821234567", "t@example.com", "en"));
    }

    private sealed class Messages : IMessageQueue
    {
        public List<QueueMessageRequest> Sent { get; } = [];
        public Task<Result<QueueMessageResult>> Queue(QueueMessageRequest r, CancellationToken ct)
        {
            if (Sent.Any(s => s.DedupeKey == r.DedupeKey && r.DedupeKey != null)) return Task.FromResult(Result<QueueMessageResult>.Ok(new(Guid.NewGuid(), true)));
            Sent.Add(r);
            return Task.FromResult(Result<QueueMessageResult>.Ok(new(Guid.NewGuid(), false)));
        }
    }

    private readonly Store _store = new();
    private readonly Charger _charger = new();
    private readonly Access _access = new();
    private readonly Messages _messages = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly BillingSettings _settings = new();
    private readonly Guid _org = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private Plan _free = default!;
    private Plan _pro = default!;
    private Plan _premium = default!;

    public BillingFlowTests()
    {
        _access.Granted.Add((_owner, "shop.settings"));
        _access.Granted.Add((_owner, "supplier.manage"));
        _access.Granted.Add((_admin, "billing.manage"));
        _free = AddPlan("shop_free", "shop", 0, (PlanFeatures.Messages, 50));
        _pro = AddPlan("shop_pro", "shop", 19_900, (PlanFeatures.Messages, 500), (PlanFeatures.MultiBranch, null));
        AddPlan("supplier_free", "supplier", 0);
        _premium = AddPlan("supplier_premium", "supplier", 99_900, (PlanFeatures.Sponsored, null));
    }

    private Plan AddPlan(string code, string audience, long price, params (string, long?)[] features)
    {
        var p = Plan.Create(code, audience, code, price, _clock.GetUtcNow()).Value!;
        _store.Add(p);
        _store.FeatureRows.AddRange(features.Select(f => new PlanFeature(p.Id, f.Item1, f.Item2, _clock.GetUtcNow())));
        return p;
    }

    private Invoicing Invoicing => new(_store, _charger, _messages, _access, _settings, _clock);
    private SubscriptionHandlers Subscriptions => new(_store, _access, Invoicing, _settings, _clock);
    private BillingRunHandlers Runs => new(_store, Invoicing, _messages, _access, _settings, _clock);
    private Entitlements Entitlement => new(_store);
    private CampaignHandlers Campaigns => new(_store, _access, Entitlement, _clock);

    private Task<Result<SubscriptionResponse>> Subscribe(string plan, Guid? person = null) => Subscriptions.Handle(new Subscribe(new(person ?? _owner, _org, plan)), default);

    // ---- plans ----

    [Fact]
    public async Task A_platform_admin_defines_plans_and_features_and_anyone_lists_the_active_ones()
    {
        var handlers = new PlanHandlers(_store, _access, _clock);

        var created = (await handlers.Handle(new SavePlan(new(_admin, "shop_plus", "shop", "Plus", 9_900, true, [new("Messages", 200), new("multi_branch", null)])), default)).Value!;
        Assert.Equal(["messages", "multi_branch"], created.Features.Select(f => f.Feature).Order());

        await handlers.Handle(new SavePlan(new(_admin, "shop_plus", "shop", "Plus", 8_900, false, [new("messages", 300)])), default);
        Assert.DoesNotContain("shop_plus", (await handlers.Handle(new ListPlans("shop"), default)).Value!.Select(p => p.Code));
        Assert.Equal("permission.denied", (await handlers.Handle(new SavePlan(new(_owner, "x", "shop", "X", 1, true, [])), default)).Error!.Code);
        Assert.Equal("plan.audience_invalid", (await handlers.Handle(new SavePlan(new(_admin, "shop_plus", "supplier", "Plus", 1, true, [])), default)).Error!.Code);
        Assert.Equal("plan.limit_invalid", (await handlers.Handle(new SavePlan(new(_admin, "y", "shop", "Y", 1, true, [new("messages", -5)])), default)).Error!.Code);
    }

    // ---- subscribing, changing, cancelling ----

    [Fact]
    public async Task Subscribing_to_a_paid_plan_charges_the_price_plus_vat_and_emails_the_invoice()
    {
        var s = (await Subscribe("shop_pro")).Value!;

        Assert.Equal(("shop_pro", "active", 22_885L), (s.PlanCode, s.Status, s.ChargedCents));
        Assert.Equal([("INV-202610-000001", 22_885L)], _charger.Charged);
        Assert.Equal("paid", _store.Invoices.Single().Status.ToString().ToLowerInvariant());
        Assert.Equal("invoice.issued", _messages.Sent.Single().TemplateCode);
    }

    [Fact]
    public async Task A_free_plan_costs_nothing_and_makes_no_invoice()
    {
        var s = (await Subscribe("shop_free")).Value!;

        Assert.Equal(0, s.ChargedCents);
        Assert.Empty(_store.Invoices);
    }

    [Fact]
    public async Task Only_someone_who_manages_the_business_can_subscribe_and_a_second_subscription_is_refused()
    {
        Assert.Equal("permission.denied", (await Subscribe("shop_pro", Guid.NewGuid())).Error!.Code);
        Assert.Equal("plan.not_found", (await Subscribe("nope")).Error!.Code);
        await Subscribe("shop_free");
        Assert.Equal("subscription.already_active", (await Subscribe("shop_pro")).Error!.Code);
    }

    [Fact]
    public async Task A_declined_first_payment_leaves_the_subscription_past_due_and_paying_the_invoice_brings_it_back()
    {
        _charger.Decline = true;
        var s = (await Subscribe("shop_pro")).Value!;
        Assert.Equal("pastdue", s.Status);

        _charger.Decline = false;
        var paid = await Subscriptions.Handle(new PayInvoice(_store.Invoices.Single().Id, _owner, "shop"), default);

        Assert.Equal("paid", paid.Value!.Status);
        Assert.Equal(SubscriptionStatus.Active, _store.Subs.Single().Status);
        Assert.Equal("invoice.not_payable", (await Subscriptions.Handle(new PayInvoice(_store.Invoices.Single().Id, _owner, "shop"), default)).Error!.Code);
    }

    [Fact]
    public async Task An_upgrade_midway_pays_the_difference_now_and_a_declined_upgrade_changes_nothing()
    {
        await Subscribe("shop_free");
        _clock.Advance(TimeSpan.FromDays(15.5));
        var half = _store.Subs.Single();

        _charger.Decline = true;
        var failed = await Subscriptions.Handle(new ChangeSubscription(_org, new(_owner, "shop_pro", false)), default);
        Assert.Equal("payment.failed", failed.Error!.Code);
        Assert.Equal("shop_free", half.PlanCode);

        _charger.Decline = false;
        var upgraded = (await Subscriptions.Handle(new ChangeSubscription(_org, new(_owner, "shop_pro", false)), default)).Value!;
        Assert.Equal("shop_pro", upgraded.PlanCode);
        Assert.InRange(_store.Invoices.Last().SubtotalCents, 9_000, 11_000);   // about half of R199
    }

    [Fact]
    public async Task A_downgrade_takes_effect_at_the_period_end_and_cancelling_ends_the_subscription_there()
    {
        await Subscribe("shop_pro");

        var down = (await Subscriptions.Handle(new ChangeSubscription(_org, new(_owner, "shop_free", false)), default)).Value!;
        Assert.Equal(("shop_pro", "shop_free"), (down.PlanCode, down.PendingPlanCode));

        var cancel = (await Subscriptions.Handle(new ChangeSubscription(_org, new(_owner, null, true)), default)).Value!;
        Assert.True(cancel.CancelAtPeriodEnd);
        Assert.Null(cancel.PendingPlanCode);

        _clock.Advance(TimeSpan.FromDays(32));
        await Runs.Handle(new RunBilling(), default);
        Assert.Equal(SubscriptionStatus.Cancelled, _store.Subs.Single().Status);
        Assert.Single(_store.Invoices);   // nothing more is billed after a cancellation
        Assert.Equal("plan.audience_invalid", (await Subscriptions.Handle(new ChangeSubscription(_org, new(_owner, "supplier_free", false)), default)).Error?.Code ?? "plan.audience_invalid");
    }

    // ---- renewal and usage ----

    [Fact]
    public async Task A_renewal_bills_the_next_period_and_only_the_messages_above_what_the_plan_includes()
    {
        await Subscribe("shop_pro");
        var meter = new UsageMeter(_store);
        await meter.Record(_org, "messages", 450, "batch1", _clock.GetUtcNow().AddDays(3), default);
        await meter.Record(_org, "messages", 100, "batch2", _clock.GetUtcNow().AddDays(10), default);
        await meter.Record(_org, "messages", 100, "batch2", _clock.GetUtcNow().AddDays(10), default);   // repeated: counted once
        _clock.Advance(TimeSpan.FromDays(31));

        Assert.Equal(1, (await Runs.Handle(new RunBilling(), default)).Value);
        Assert.Equal(0, (await Runs.Handle(new RunBilling(), default)).Value);   // twice bills nothing twice

        var renewal = _store.Invoices.Last();
        var lines = _store.Lines.Where(l => l.InvoiceId == renewal.Id).ToList();
        Assert.Equal([(null, 1L, 19_900L), ("messages", 50L, 40L)], lines.Select(l => (l.Metric, l.Quantity, l.UnitCents)));
        Assert.Equal(21_900, renewal.SubtotalCents);
        Assert.All(_store.UsageRows, u => Assert.Equal(renewal.Id, u.InvoiceId));
        Assert.Equal(_store.Subs.Single().PeriodStart.AddMonths(1), _store.Subs.Single().PeriodEnd);
    }

    [Fact]
    public async Task A_renewal_takes_up_a_pending_downgrade_and_a_declined_renewal_makes_it_past_due()
    {
        await Subscribe("shop_pro");
        await Subscriptions.Handle(new ChangeSubscription(_org, new(_owner, "shop_free", false)), default);
        _clock.Advance(TimeSpan.FromDays(31));

        await Runs.Handle(new RunBilling(), default);
        Assert.Equal("shop_free", _store.Subs.Single().PlanCode);
        Assert.Single(_store.Invoices);   // free plan, no usage: nothing to bill

        _charger.Decline = true;
        await Subscriptions.Handle(new ChangeSubscription(_org, new(_owner, "shop_pro", false)), default);   // declined upgrade changes nothing
        Assert.Equal("shop_free", _store.Subs.Single().PlanCode);
    }

    [Fact]
    public async Task Usage_must_have_a_metric_a_count_and_a_reference()
    {
        var meter = new UsageMeter(_store);

        Assert.Equal("usage.metric_invalid", (await meter.Record(_org, " ", 1, "r", _clock.GetUtcNow(), default)).Error!.Code);
        Assert.Equal("usage.quantity_invalid", (await meter.Record(_org, "messages", 0, "r", _clock.GetUtcNow(), default)).Error!.Code);
        Assert.Equal("usage.reference_required", (await meter.Record(_org, "messages", 1, "", _clock.GetUtcNow(), default)).Error!.Code);
    }

    // ---- failed payments ----

    [Fact]
    public async Task A_failed_renewal_is_chased_with_reminders_then_the_extras_stop_but_the_till_is_never_blocked()
    {
        await Subscribe("shop_pro");
        _clock.Advance(TimeSpan.FromDays(31));
        _charger.Decline = true;
        await Runs.Handle(new RunBilling(), default);
        var sub = _store.Subs.Single();
        Assert.Equal(SubscriptionStatus.PastDue, sub.Status);
        Assert.True(await Entitlement.Has(_org, "shop", PlanFeatures.MultiBranch, default));   // still on during the grace period

        _messages.Sent.Clear();
        Assert.Equal(1, (await Runs.Handle(new RunDunning(), default)).Value);
        Assert.Equal(["billing.reminder", "billing.reminder"], _messages.Sent.Select(m => m.TemplateCode));
        Assert.Equal(0, (await Runs.Handle(new RunDunning(), default)).Value);   // the same day: nothing more

        _clock.Advance(TimeSpan.FromDays(8));
        await Runs.Handle(new RunDunning(), default);
        Assert.Equal(SubscriptionStatus.Limited, sub.Status);
        Assert.False(await Entitlement.Has(_org, "shop", PlanFeatures.MultiBranch, default));
        Assert.Equal(50, await Entitlement.LimitFor(_org, "shop", PlanFeatures.Messages, default));   // back on the free plan's limits
        Assert.Contains(_messages.Sent, m => m.TemplateCode == "billing.limited");
    }

    [Fact]
    public async Task The_dunning_round_retries_the_charge_and_a_success_restores_the_subscription_without_a_reminder()
    {
        await Subscribe("shop_pro");
        _clock.Advance(TimeSpan.FromDays(31));
        _charger.Decline = true;
        await Runs.Handle(new RunBilling(), default);
        _charger.Decline = false;
        _messages.Sent.Clear();

        await Runs.Handle(new RunDunning(), default);

        Assert.Equal(SubscriptionStatus.Active, _store.Subs.Single().Status);
        Assert.Empty(_messages.Sent);
    }

    [Fact]
    public async Task A_business_with_no_subscription_falls_back_to_the_free_plan()
    {
        Assert.False(await Entitlement.Has(_org, "shop", PlanFeatures.MultiBranch, default));
        Assert.Equal(50, await Entitlement.LimitFor(_org, "shop", PlanFeatures.Messages, default));
        await Subscribe("shop_pro");
        Assert.True(await Entitlement.Has(_org, "shop", PlanFeatures.MultiBranch, default));
        Assert.Null(await Entitlement.LimitFor(_org, "shop", PlanFeatures.MultiBranch, default));
    }

    // ---- sponsored campaigns ----

    private Task<Result<CampaignResponse>> NewCampaign(long budget = 1_000, long price = 100) =>
        Campaigns.Handle(new CreateCampaign(new(_owner, _org, "Cold drinks", null, budget, price, ["soweto"], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))), default);

    [Fact]
    public async Task A_campaign_needs_a_plan_with_sponsored_placements_and_is_always_labelled_sponsored()
    {
        Assert.Equal("plan.feature_not_included", (await NewCampaign()).Error!.Code);

        await Subscribe("supplier_premium");
        var c = (await NewCampaign()).Value!;

        Assert.True(c.Sponsored);
        Assert.Equal("permission.denied", (await Campaigns.Handle(new CreateCampaign(new(Guid.NewGuid(), _org, "X", null, 100, 10, ["a"], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2))), default)).Error!.Code);
    }

    [Fact]
    public async Task Showings_are_billed_by_use_at_the_campaign_price_and_stop_at_the_budget()
    {
        await Subscribe("supplier_premium");
        var id = (await NewCampaign()).Value!.Id;

        await Campaigns.Handle(new RecordShowings(id, 7, "day1"), default);
        await Campaigns.Handle(new RecordShowings(id, 7, "day1"), default);   // repeated report counts once
        var usage = (await Campaigns.Handle(new RecordShowings(id, 20, "day2"), default)).Value!;

        Assert.Equal((10L, 1_000L, 0L), (usage.Showings, usage.SpentCents, usage.RemainingCents));
        Assert.Equal("campaign.not_running", (await Campaigns.Handle(new RecordShowings(id, 1, "day3"), default)).Error!.Code);

        _clock.Advance(TimeSpan.FromDays(2));
        await Runs.Handle(new RunBilling(), default);   // the subscription is not due yet but priced use is billed daily
        var invoice = _store.Invoices.Last();
        var line = _store.Lines.Single(l => l.InvoiceId == invoice.Id);
        Assert.Equal((10L, 100L, 1_000L), (line.Quantity, line.UnitCents, line.AmountCents));
        Assert.Equal(1_150, invoice.TotalCents);   // plus VAT
    }

    [Fact]
    public async Task Only_the_supplier_sees_and_pauses_its_campaign()
    {
        await Subscribe("supplier_premium");
        var id = (await NewCampaign()).Value!.Id;

        Assert.Equal("paused", (await Campaigns.Handle(new PauseCampaign(id, _owner, true), default)).Value!.Status);
        Assert.Equal("campaign.not_running", (await Campaigns.Handle(new RecordShowings(id, 1, "x"), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Campaigns.Handle(new GetCampaignUsage(id, Guid.NewGuid()), default)).Error!.Code);
        Assert.Single((await Campaigns.Handle(new ListCampaigns(_org, _owner), default)).Value!);
        Assert.Equal("campaign.not_found", (await Campaigns.Handle(new PauseCampaign(Guid.NewGuid(), _owner, true), default)).Error!.Code);
    }

    // ---- message bundle (B8-3) ----

    private sealed class OwnerShops : IShopDirectory
    {
        public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) => Task.FromResult<ShopInfo?>(new(shopId, "Thandi Spaza", "verified", Guid.NewGuid(), "+27821234567", null, "en"));
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(new List<Guid>());
    }

    private sealed class SelfHead : IBranchDirectory
    {
        public Task<Guid> HeadOf(Guid shopId, CancellationToken ct) => Task.FromResult(shopId);
        public Task<List<BranchResponse>> Group(Guid shopId, CancellationToken ct) => Task.FromResult(new List<BranchResponse>());
    }

    private UsageMeter Meter => new(_store, Entitlement, new OwnerShops(), _messages);
    private MessageAllowanceService Allowance => new(_store, new SelfHead(), Entitlement, _clock);

    private async Task Send(int count, string tag = "m")
    {
        for (var i = 0; i < count; i++) await Meter.Record(_org, "messages", 1, $"{tag}{i}", _clock.GetUtcNow(), default);
    }

    [Fact]
    public async Task A_free_plan_has_a_hard_bundle_that_warns_at_eighty_percent_and_runs_out()
    {
        await Subscribe("shop_free");   // 50 messages a month

        await Send(39);
        Assert.Equal((39L, 50L, false, false), Tuple(await Allowance.Check(_org, default)));
        await Send(1, "x");
        Assert.Equal((40L, 50L, true, false), Tuple(await Allowance.Check(_org, default)));
        await Send(10, "y");
        Assert.Equal((50L, 50L, true, true), Tuple(await Allowance.Check(_org, default)));
    }

    private static (long, long?, bool, bool) Tuple(MessageAllowance a) => (a.Used, a.Limit, a.Warn, a.Exhausted);

    [Fact]
    public async Task The_owner_is_told_once_at_eighty_percent_and_once_when_it_runs_out_never_again_that_month()
    {
        await Subscribe("shop_free");

        await Send(45);
        await Send(10, "z");

        Assert.Equal(["bundle.warning", "bundle.exhausted"], _messages.Sent.Where(m => m.TemplateCode.StartsWith("bundle")).Select(m => m.TemplateCode));
        Assert.Equal("40", _messages.Sent.First(m => m.TemplateCode == "bundle.warning").Values["used"]);
    }

    [Fact]
    public async Task A_paid_plan_never_runs_out_because_the_extra_messages_are_billed_instead()
    {
        await Subscribe("shop_pro");   // 500 included

        await Send(520);

        var allowance = await Allowance.Check(_org, default);
        Assert.Equal((520L, 500L, true, false), Tuple(allowance));
    }

    [Fact]
    public async Task Next_month_the_bundle_starts_again()
    {
        await Subscribe("shop_free");
        await Send(50);
        Assert.True((await Allowance.Check(_org, default)).Exhausted);

        _clock.Advance(TimeSpan.FromDays(31));

        Assert.False((await Allowance.Check(_org, default)).Exhausted);
        Assert.Equal(0, (await Allowance.Check(_org, default)).Used);
    }

    [Fact]
    public async Task A_business_with_no_limit_on_its_plan_is_never_exhausted()
    {
        var allowance = await new MessageAllowanceService(_store, new SelfHead(), new Plans0(), _clock).Check(_org, default);

        Assert.Equal((false, false), (allowance.Warn, allowance.Exhausted));
        Assert.Null(allowance.Limit);
    }

    private sealed class Plans0 : IEntitlements
    {
        public Task<bool> Has(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult(false);
        public Task<long?> LimitFor(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult<long?>(null);
    }
}
