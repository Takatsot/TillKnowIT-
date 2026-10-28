using Billing.Contracts;
using Billing.Domain;
using Identity.Contracts;
using Messaging.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Billing.Application;

public interface IBillingStore
{
    void Add(Plan plan);
    Task<Plan?> PlanByCode(string code, CancellationToken ct);
    Task<Plan?> PlanById(Guid id, CancellationToken ct);
    Task<List<Plan>> Plans(string? audience, bool activeOnly, CancellationToken ct);
    Task<Plan?> FreePlan(string audience, CancellationToken ct);
    Task<Dictionary<Guid, List<PlanFeature>>> FeaturesOf(IReadOnlyCollection<Guid> planIds, CancellationToken ct);
    Task ReplaceFeatures(Guid planId, IReadOnlyList<PlanFeatureDto> features, DateTimeOffset now, CancellationToken ct);

    void Add(Subscription subscription);
    Task<Subscription?> SubscriptionOf(Guid organisationId, CancellationToken ct);
    Task<Subscription?> SubscriptionById(Guid id, CancellationToken ct);
    Task<List<Subscription>> DueSubscriptions(DateTimeOffset now, int take, CancellationToken ct);
    Task<List<Subscription>> PastDue(int take, CancellationToken ct);

    void Add(Invoice invoice, IReadOnlyList<InvoiceLine> lines);
    Task<Invoice?> InvoiceByReference(string reference, CancellationToken ct);
    Task<Invoice?> InvoiceById(Guid id, CancellationToken ct);
    Task<List<Invoice>> InvoicesOf(Guid organisationId, int take, CancellationToken ct);
    Task<Invoice?> LatestFailed(Guid organisationId, CancellationToken ct);
    Task<List<InvoiceLine>> LinesOf(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct);
    Task<int> InvoiceCount(CancellationToken ct);

    void Add(UsageRecord usage);
    Task<UsageRecord?> UsageByReference(Guid organisationId, string reference, CancellationToken ct);
    Task<List<UsageRecord>> UnbilledUsage(Guid organisationId, DateTimeOffset before, CancellationToken ct);
    Task<List<Guid>> OrganisationsWithUnbilledPricedUsage(DateTimeOffset before, int take, CancellationToken ct);

    void Add(SponsoredCampaign campaign);
    Task<SponsoredCampaign?> Campaign(Guid id, CancellationToken ct);
    Task<List<SponsoredCampaign>> CampaignsOf(Guid supplierOrganisationId, CancellationToken ct);
    Task<List<SponsoredCampaign>> RunningOn(DateOnly day, CancellationToken ct);

    Task Save(CancellationToken ct);
}

/// <summary>Takes money for an invoice through the payment gateway. The module never holds card details; a failed charge is a normal outcome, not an error.</summary>
public interface IBillingCharger
{
    Task<Result> Charge(Guid organisationId, string invoiceNumber, long totalCents, string reference, CancellationToken ct);
}

internal static class BillingAccess
{
    public static string PermissionFor(string audience) => audience == Audience.Supplier ? "supplier.manage" : "shop.settings";

    public static async Task<bool> Platform(IPermissionChecker permissions, Guid personId, CancellationToken ct) => await permissions.Has(personId, "billing.manage", ct);

    public static Result Denied() => Result.Fail("permission.denied", "You are not allowed to manage billing for this business.");

    public static string Mask(Guid id) => id.ToString("N")[..8];
}

// ---------------------------------------------------------------- G3-1: plans

public sealed record SavePlan(SavePlanRequest Request) : ICommand<PlanResponse>;
public sealed record ListPlans(string? Audience) : IQuery<List<PlanResponse>>;

internal sealed class PlanHandlers(IBillingStore store, IPermissionChecker permissions, TimeProvider clock) : ICommandHandler<SavePlan, PlanResponse>, IQueryHandler<ListPlans, List<PlanResponse>>
{
    public async Task<Result<PlanResponse>> Handle(SavePlan c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await BillingAccess.Platform(permissions, r.CallerPersonId, ct)) return Result<PlanResponse>.Fail("permission.denied", "Only a platform admin can manage plans.");
        var features = (r.Features ?? []).Where(f => !string.IsNullOrWhiteSpace(f.Feature)).GroupBy(f => f.Feature.Trim().ToLowerInvariant()).Select(g => new PlanFeatureDto(g.Key, g.First().Limit)).ToList();
        if (features.Any(f => f.Limit is < 0)) return Result<PlanResponse>.Fail("plan.limit_invalid", "A limit cannot be negative.");

        var plan = await store.PlanByCode(r.Code ?? "", ct);
        if (plan is null)
        {
            var created = Plan.Create(r.Code, r.Audience, r.Name, r.PriceCents, clock.GetUtcNow());
            if (!created.IsSuccess) return Result<PlanResponse>.Fail(created.Error!.Code, created.Error.Message);
            plan = created.Value!;
            store.Add(plan);
        }
        else
        {
            if (plan.Audience != r.Audience) return Result<PlanResponse>.Fail("plan.audience_invalid", "A plan cannot change who it is for.");
            var updated = plan.Update(r.Name, r.PriceCents, r.IsActive);
            if (!updated.IsSuccess) return Result<PlanResponse>.Fail(updated.Error!.Code, updated.Error.Message);
        }
        await store.ReplaceFeatures(plan.Id, features, clock.GetUtcNow(), ct);
        await store.Save(ct);
        return Result<PlanResponse>.Ok(new(plan.Id, plan.Code, plan.Audience, plan.Name, plan.PriceCents, plan.IsActive, features));
    }

    public async Task<Result<List<PlanResponse>>> Handle(ListPlans q, CancellationToken ct)
    {
        if (q.Audience is not null && !Audience.IsValid(q.Audience)) return Result<List<PlanResponse>>.Fail("plan.audience_invalid", "A plan is for shops or suppliers.");
        var plans = await store.Plans(q.Audience, activeOnly: true, ct);
        var features = await store.FeaturesOf(plans.Select(p => p.Id).ToList(), ct);
        return Result<List<PlanResponse>>.Ok(plans.OrderBy(p => p.Audience).ThenBy(p => p.PriceCents)
            .Select(p => new PlanResponse(p.Id, p.Code, p.Audience, p.Name, p.PriceCents, p.IsActive,
                features.GetValueOrDefault(p.Id, []).OrderBy(f => f.Feature).Select(f => new PlanFeatureDto(f.Feature, f.Limit)).ToList())).ToList());
    }
}

// ---------------------------------------------------------------- invoices and charging (shared)

/// <summary>Makes, emails and charges invoices. Used by subscribing, changing plan, renewal and paying again.</summary>
public sealed class Invoicing(IBillingStore store, IBillingCharger charger, IMessageQueue queue, IPersonDirectory people, BillingSettings settings, TimeProvider clock)
{
    public async Task<Invoice> Issue(Guid organisationId, Subscription? subscription, string reference, List<InvoiceLine> lines, CancellationToken ct)
    {
        var existing = await store.InvoiceByReference(reference, ct);
        if (existing is not null) return existing;
        var now = clock.GetUtcNow();
        var number = $"INV-{now:yyyyMM}-{await store.InvoiceCount(ct) + 1:D6}";
        var invoice = Invoice.Issue(number, reference, organisationId, subscription?.Id, lines, settings, now);
        store.Add(invoice, lines);
        return invoice;
    }

    /// <summary>Takes the money. Nothing to pay (or a free plan) is paid at once. A failed charge marks the invoice failed and the subscription past due; paying brings it back.</summary>
    public async Task<Result> Collect(Invoice invoice, Subscription? subscription, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (invoice.TotalCents == 0)
        {
            invoice.MarkPaid(now);
            subscription?.PaymentReceived();
            return Result.Ok();
        }
        var outcome = await charger.Charge(invoice.OrganisationId, invoice.Number, invoice.TotalCents, invoice.Reference, ct);
        if (outcome.IsSuccess)
        {
            invoice.MarkPaid(now);
            subscription?.PaymentReceived();
            return Result.Ok();
        }
        invoice.MarkFailed();
        subscription?.PaymentFailed(now);
        return Result.Fail("payment.failed", outcome.Error!.Message);
    }

    public async Task Email(Invoice invoice, Guid personId, string? shopName, CancellationToken ct)
    {
        var person = await people.Get(personId, ct);
        if (person?.Email is not { Length: > 0 } email) return;
        await queue.Queue(new QueueMessageRequest("invoice.issued", person.Language, "email", email, new()
        {
            ["name"] = person.DisplayName, ["number"] = invoice.Number, ["total"] = Money(invoice.TotalCents), ["vat"] = Money(invoice.VatCents),
            ["status"] = invoice.Status.ToString().ToLowerInvariant(), ["due"] = invoice.DueAt.ToString("d MMMM yyyy"),
        }, null, $"invoice:{invoice.Number}"), ct);
    }

    public static string Money(long cents) => $"R{cents / 100}.{cents % 100:D2}";

    /// <summary>
    /// Lines for what was used in a period. Use with its own price is billed in full; the rest is billed only above what the plan includes.
    /// The quantity on a line is always what was recorded, so the invoice equals the usage.
    /// </summary>
    public List<InvoiceLine> UsageLines(IReadOnlyList<UsageRecord> usage, IReadOnlyDictionary<string, long?> included, DateTimeOffset now)
    {
        var lines = new List<InvoiceLine>();
        foreach (var priced in usage.Where(u => u.UnitCents is not null).GroupBy(u => (u.Metric, u.UnitCents)).OrderBy(g => g.Key.Metric))
            lines.Add(new($"{priced.Key.Metric} (used)", priced.Key.Metric, priced.Sum(u => u.Quantity), priced.Key.UnitCents!.Value, now));
        foreach (var plain in usage.Where(u => u.UnitCents is null).GroupBy(u => u.Metric).OrderBy(g => g.Key))
        {
            var (billable, _) = BillingMath.Overage(plain.Sum(u => u.Quantity), included.GetValueOrDefault(plain.Key), settings.UnitPrice(plain.Key));
            if (billable > 0) lines.Add(new($"{plain.Key} above the {included.GetValueOrDefault(plain.Key) ?? 0} included", plain.Key, billable, settings.UnitPrice(plain.Key), now));
        }
        return lines;
    }
}

// ---------------------------------------------------------------- G3-2: subscribe, change, cancel

public sealed record Subscribe(SubscribeRequest Request) : ICommand<SubscriptionResponse>;
public sealed record ChangeSubscription(Guid OrganisationId, ChangeSubscriptionRequest Request) : ICommand<SubscriptionResponse>;
public sealed record GetSubscription(Guid OrganisationId, Guid PersonId, string Audience) : IQuery<SubscriptionResponse>;
public sealed record ListInvoices(Guid OrganisationId, Guid PersonId, string Audience) : IQuery<List<InvoiceResponse>>;
public sealed record PayInvoice(Guid InvoiceId, Guid PersonId, string Audience) : ICommand<InvoiceResponse>;

internal sealed class SubscriptionHandlers(IBillingStore store, IOrganisationAccess access, Invoicing invoicing, BillingSettings settings, TimeProvider clock)
    : ICommandHandler<Subscribe, SubscriptionResponse>, ICommandHandler<ChangeSubscription, SubscriptionResponse>, IQueryHandler<GetSubscription, SubscriptionResponse>,
      IQueryHandler<ListInvoices, List<InvoiceResponse>>, ICommandHandler<PayInvoice, InvoiceResponse>
{
    private async Task<SubscriptionResponse> Respond(Subscription s, Invoice? invoice, CancellationToken ct) =>
        new(s.Id, s.OrganisationId, s.PlanCode, s.Status.ToString().ToLowerInvariant(), s.PeriodStart, s.PeriodEnd,
            s.PendingPlanId is { } pid ? (await store.PlanById(pid, ct))?.Code : null, s.CancelAtPeriodEnd, invoice?.Id, invoice?.TotalCents ?? 0);

    private static InvoiceResponse Respond(Invoice i, IEnumerable<InvoiceLine> lines) =>
        new(i.Id, i.Number, i.Status.ToString().ToLowerInvariant(), i.SubtotalCents, i.VatPercent, i.VatCents, i.TotalCents, i.DueAt, i.PaidAt,
            lines.Select(l => new InvoiceLineResponse(l.Description, l.Metric, l.Quantity, l.UnitCents, l.AmountCents)).ToList());

    public async Task<Result<SubscriptionResponse>> Handle(Subscribe c, CancellationToken ct)
    {
        var r = c.Request;
        var plan = await store.PlanByCode(r.PlanCode ?? "", ct);
        if (plan is null || !plan.IsActive) return Result<SubscriptionResponse>.Fail("plan.not_found", "That plan is not available.");
        if (!await access.Has(r.PersonId, r.OrganisationId, BillingAccess.PermissionFor(plan.Audience), ct)) return Result<SubscriptionResponse>.Fail("permission.denied", "You are not allowed to manage billing for this business.");

        var now = clock.GetUtcNow();
        var existing = await store.SubscriptionOf(r.OrganisationId, ct);
        Subscription subscription;
        if (existing is null)
        {
            subscription = Subscription.Start(r.OrganisationId, plan, r.PersonId, now);
            store.Add(subscription);
        }
        else
        {
            var restarted = existing.Restart(plan, r.PersonId, now);
            if (!restarted.IsSuccess) return Result<SubscriptionResponse>.Fail(restarted.Error!.Code, restarted.Error.Message);
            subscription = existing;
        }

        Invoice? invoice = null;
        if (!plan.IsFree)
        {
            invoice = await invoicing.Issue(r.OrganisationId, subscription, $"start:{subscription.Id}:{subscription.PeriodStart:yyyyMMddHHmmss}",
                [new($"{plan.Name}, {subscription.PeriodStart:d MMM yyyy} to {subscription.PeriodEnd:d MMM yyyy}", null, 1, plan.PriceCents, now)], ct);
            await invoicing.Collect(invoice, subscription, ct);
        }
        await store.Save(ct);
        if (invoice is not null) await invoicing.Email(invoice, r.PersonId, null, ct);
        return Result<SubscriptionResponse>.Ok(await Respond(subscription, invoice, ct));
    }

    public async Task<Result<SubscriptionResponse>> Handle(ChangeSubscription c, CancellationToken ct)
    {
        var r = c.Request;
        var subscription = await store.SubscriptionOf(c.OrganisationId, ct);
        if (subscription is null) return Result<SubscriptionResponse>.Fail("subscription.not_found", "This business has no subscription.");
        var current = await store.PlanById(subscription.PlanId, ct);
        if (!await access.Has(r.PersonId, c.OrganisationId, BillingAccess.PermissionFor(current!.Audience), ct)) return Result<SubscriptionResponse>.Fail("permission.denied", "You are not allowed to manage billing for this business.");

        if (r.Cancel)
        {
            var cancelled = subscription.Cancel();
            if (!cancelled.IsSuccess) return Result<SubscriptionResponse>.Fail(cancelled.Error!.Code, cancelled.Error.Message);
            await store.Save(ct);
            return Result<SubscriptionResponse>.Ok(await Respond(subscription, null, ct));
        }

        var plan = await store.PlanByCode(r.PlanCode ?? "", ct);
        if (plan is null || !plan.IsActive) return Result<SubscriptionResponse>.Fail("plan.not_found", "That plan is not available.");
        if (plan.Audience != current.Audience) return Result<SubscriptionResponse>.Fail("plan.audience_invalid", "That plan is for a different kind of business.");
        if (subscription.Status == SubscriptionStatus.Cancelled) return Result<SubscriptionResponse>.Fail("subscription.cancelled", "This subscription was cancelled. Subscribe again.");

        var now = clock.GetUtcNow();
        Invoice? invoice = null;
        var upgrade = plan.PriceCents > subscription.PriceCents;
        if (upgrade)
        {
            // A dearer plan starts now and pays the difference for the rest of the period. If the payment fails nothing changes.
            var charge = BillingMath.UpgradeCharge(subscription.PriceCents, plan.PriceCents, now, subscription.PeriodStart, subscription.PeriodEnd);
            if (charge > 0)
            {
                invoice = await invoicing.Issue(c.OrganisationId, subscription, $"upgrade:{subscription.Id}:{plan.Code}:{now:yyyyMMddHHmmss}",
                    [new($"Upgrade to {plan.Name} for the rest of the period", null, 1, charge, now)], ct);
                var paid = await invoicing.Collect(invoice, null, ct);
                await store.Save(ct);
                if (!paid.IsSuccess) return Result<SubscriptionResponse>.Fail(paid.Error!.Code, paid.Error.Message);
            }
        }
        var changed = subscription.Change(plan, atPeriodEnd: !upgrade && settings.DowngradeAtPeriodEnd);
        if (!changed.IsSuccess) return Result<SubscriptionResponse>.Fail(changed.Error!.Code, changed.Error.Message);
        await store.Save(ct);
        if (invoice is not null) await invoicing.Email(invoice, r.PersonId, null, ct);
        return Result<SubscriptionResponse>.Ok(await Respond(subscription, invoice, ct));
    }

    public async Task<Result<SubscriptionResponse>> Handle(GetSubscription q, CancellationToken ct)
    {
        if (!await access.Has(q.PersonId, q.OrganisationId, BillingAccess.PermissionFor(q.Audience), ct)) return Result<SubscriptionResponse>.Fail("permission.denied", "You are not allowed to see billing for this business.");
        var subscription = await store.SubscriptionOf(q.OrganisationId, ct);
        return subscription is null ? Result<SubscriptionResponse>.Fail("subscription.not_found", "This business has no subscription.") : Result<SubscriptionResponse>.Ok(await Respond(subscription, null, ct));
    }

    public async Task<Result<List<InvoiceResponse>>> Handle(ListInvoices q, CancellationToken ct)
    {
        if (!await access.Has(q.PersonId, q.OrganisationId, BillingAccess.PermissionFor(q.Audience), ct)) return Result<List<InvoiceResponse>>.Fail("permission.denied", "You are not allowed to see billing for this business.");
        var invoices = await store.InvoicesOf(q.OrganisationId, 100, ct);
        var lines = (await store.LinesOf(invoices.Select(i => i.Id).ToList(), ct)).ToLookup(l => l.InvoiceId);
        return Result<List<InvoiceResponse>>.Ok(invoices.Select(i => Respond(i, lines[i.Id])).ToList());
    }

    /// <summary>Pays a failed invoice again (for example after topping up a card). Paying brings the subscription back to active at once.</summary>
    public async Task<Result<InvoiceResponse>> Handle(PayInvoice c, CancellationToken ct)
    {
        var invoice = await store.InvoiceById(c.InvoiceId, ct);
        if (invoice is null) return Result<InvoiceResponse>.Fail("invoice.not_found", "Invoice not found.");
        if (!await access.Has(c.PersonId, invoice.OrganisationId, BillingAccess.PermissionFor(c.Audience), ct)) return Result<InvoiceResponse>.Fail("permission.denied", "You are not allowed to pay for this business.");
        if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Void) return Result<InvoiceResponse>.Fail("invoice.not_payable", "This invoice is not waiting for payment.");

        var subscription = await store.SubscriptionOf(invoice.OrganisationId, ct);
        var paid = await invoicing.Collect(invoice, subscription, ct);
        await store.Save(ct);
        var lines = await store.LinesOf([invoice.Id], ct);
        return paid.IsSuccess ? Result<InvoiceResponse>.Ok(Respond(invoice, lines)) : Result<InvoiceResponse>.Fail(paid.Error!.Code, paid.Error.Message);
    }
}

// ---------------------------------------------------------------- usage and entitlements

public sealed class UsageMeter(IBillingStore store) : IUsageMeter
{
    public async Task<Result> Record(Guid organisationId, string metric, long quantity, string reference, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (await store.UsageByReference(organisationId, reference, ct) is not null) return Result.Ok();
        var usage = UsageRecord.Create(organisationId, metric, quantity, reference, occurredAt);
        if (!usage.IsSuccess) return Result.Fail(usage.Error!.Code, usage.Error.Message);
        store.Add(usage.Value!);
        await store.Save(ct);
        return Result.Ok();
    }
}

public sealed class Entitlements(IBillingStore store) : IEntitlements
{
    private async Task<Dictionary<string, long?>> Features(Guid organisationId, string audience, CancellationToken ct)
    {
        var subscription = await store.SubscriptionOf(organisationId, ct);
        var plan = subscription is { GivesPaidFeatures: true } ? await store.PlanById(subscription.PlanId, ct) : await store.FreePlan(audience, ct);
        if (plan is null || plan.Audience != audience) plan = await store.FreePlan(audience, ct);
        return plan is null ? [] : (await store.FeaturesOf([plan.Id], ct)).GetValueOrDefault(plan.Id, []).ToDictionary(f => f.Feature, f => f.Limit);
    }

    public async Task<bool> Has(Guid organisationId, string audience, string feature, CancellationToken ct) => (await Features(organisationId, audience, ct)).ContainsKey(feature);

    public async Task<long?> LimitFor(Guid organisationId, string audience, string feature, CancellationToken ct) => (await Features(organisationId, audience, ct)).GetValueOrDefault(feature);
}

// ---------------------------------------------------------------- renewal, billing by use, dunning (run by the worker)

public sealed record RunBilling : ICommand<int>;
public sealed record RunDunning : ICommand<int>;

internal sealed class BillingRunHandlers(IBillingStore store, Invoicing invoicing, IMessageQueue queue, IPersonDirectory people, BillingSettings settings, TimeProvider clock)
    : ICommandHandler<RunBilling, int>, ICommandHandler<RunDunning, int>
{
    /// <summary>
    /// Renews every subscription whose period has ended: the closing period's usage above what the plan includes, plus the next period's price.
    /// Then bills priced use (sponsored showings) of businesses with no subscription due. Running it twice bills nothing twice.
    /// </summary>
    public async Task<Result<int>> Handle(RunBilling c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var made = 0;
        foreach (var s in await store.DueSubscriptions(now, 200, ct))
        {
            var closing = await store.PlanById(s.PlanId, ct);
            var included = (await store.FeaturesOf([s.PlanId], ct)).GetValueOrDefault(s.PlanId, []).ToDictionary(f => f.Feature, f => f.Limit);
            var periodEnd = s.PeriodEnd;
            var usage = await store.UnbilledUsage(s.OrganisationId, periodEnd, ct);
            var lines = invoicing.UsageLines(usage, included, now);

            var pending = s.PendingPlanId is { } pid ? await store.PlanById(pid, ct) : null;
            var renewed = s.Renew(pending);
            if (renewed && s.PriceCents > 0) lines.Insert(0, new($"{(pending ?? closing)!.Name}, {s.PeriodStart:d MMM yyyy} to {s.PeriodEnd:d MMM yyyy}", null, 1, s.PriceCents, now));

            if (lines.Count > 0)
            {
                var invoice = await invoicing.Issue(s.OrganisationId, s, $"renewal:{s.Id}:{periodEnd:yyyyMMddHHmmss}", lines, ct);
                foreach (var u in usage) u.Bill(invoice.Id);
                await invoicing.Collect(invoice, s, ct);
                await store.Save(ct);
                await invoicing.Email(invoice, s.BillingPersonId, null, ct);
                made++;
            }
            else await store.Save(ct);
        }

        var dayStart = new DateTimeOffset(now.ToOffset(BusinessDay.Offset).Date, BusinessDay.Offset).ToUniversalTime();
        foreach (var org in await store.OrganisationsWithUnbilledPricedUsage(dayStart, 200, ct))
        {
            var usage = (await store.UnbilledUsage(org, dayStart, ct)).Where(u => u.UnitCents is not null).ToList();
            if (usage.Count == 0) continue;
            var lines = invoicing.UsageLines(usage, new Dictionary<string, long?>(), now);
            var invoice = await invoicing.Issue(org, null, $"usage:{org:N}:{dayStart:yyyyMMdd}", lines, ct);
            foreach (var u in usage) u.Bill(invoice.Id);
            await invoicing.Collect(invoice, null, ct);
            await store.Save(ct);
            var owner = (await store.SubscriptionOf(org, ct))?.BillingPersonId;
            if (owner is { } person) await invoicing.Email(invoice, person, null, ct);
            made++;
        }
        return Result<int>.Ok(made);
    }

    /// <summary>For subscriptions that failed to pay: tries the charge again, sends the reminders owed, and when the grace period ends switches the plan's extra features off. Never touches selling.</summary>
    public async Task<Result<int>> Handle(RunDunning c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var sent = 0;
        foreach (var s in await store.PastDue(200, ct))
        {
            var failed = await store.LatestFailed(s.OrganisationId, ct);
            if (failed is not null && (await invoicing.Collect(failed, s, ct)).IsSuccess)
            {
                await store.Save(ct);
                continue;
            }

            var (reminder, limited) = s.Dunning(now, settings);
            var person = reminder is not null || limited ? await people.Get(s.BillingPersonId, ct) : null;
            if (person is not null)
            {
                var template = limited ? "billing.limited" : "billing.reminder";
                var key = limited ? $"billing:{s.Id:N}:limited:{s.PastDueSince:yyyyMMdd}" : $"billing:{s.Id:N}:reminder{reminder}:{s.PastDueSince:yyyyMMdd}";
                var values = new Dictionary<string, string>
                {
                    ["name"] = person.DisplayName, ["plan"] = s.PlanCode, ["days"] = Math.Max(0, settings.GraceDays - (int)(now - s.PastDueSince!.Value).TotalDays).ToString(),
                    ["total"] = failed is null ? "" : Invoicing.Money(failed.TotalCents),
                };
                foreach (var channel in new[] { "whatsapp", "email" })
                {
                    var destination = channel == "email" ? person.Email : person.Phone;
                    if (string.IsNullOrWhiteSpace(destination)) continue;
                    await queue.Queue(new QueueMessageRequest(template, person.Language, channel, destination, values, null, $"{key}:{channel}"), ct);
                }
                sent++;
            }
            await store.Save(ct);
        }
        return Result<int>.Ok(sent);
    }
}

// ---------------------------------------------------------------- G3-3: sponsored campaigns

public sealed record CreateCampaign(CreateCampaignRequest Request) : ICommand<CampaignResponse>;
public sealed record PauseCampaign(Guid CampaignId, Guid PersonId, bool Paused) : ICommand<CampaignResponse>;
public sealed record RecordShowings(Guid CampaignId, long Showings, string Reference) : ICommand<CampaignUsageResponse>;
public sealed record GetCampaignUsage(Guid CampaignId, Guid PersonId) : IQuery<CampaignUsageResponse>;
public sealed record ListCampaigns(Guid SupplierOrganisationId, Guid PersonId) : IQuery<List<CampaignResponse>>;

internal sealed class CampaignHandlers(IBillingStore store, IOrganisationAccess access, IEntitlements entitlements, TimeProvider clock)
    : ICommandHandler<CreateCampaign, CampaignResponse>, ICommandHandler<PauseCampaign, CampaignResponse>, ICommandHandler<RecordShowings, CampaignUsageResponse>,
      IQueryHandler<GetCampaignUsage, CampaignUsageResponse>, IQueryHandler<ListCampaigns, List<CampaignResponse>>
{
    public const string Feature = PlanFeatures.Sponsored;
    public const string Metric = "sponsored_showings";

    private static CampaignResponse Respond(SponsoredCampaign c) =>
        new(c.Id, c.Name, c.Status.ToString().ToLowerInvariant(), c.BudgetCents, c.SpentCents, c.AreaList.ToList(), c.StartsOn, c.EndsOn, Sponsored: true);

    public async Task<Result<CampaignResponse>> Handle(CreateCampaign c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await access.Has(r.PersonId, r.SupplierOrganisationId, "supplier.manage", ct)) return Result<CampaignResponse>.Fail("permission.denied", "You are not allowed to run campaigns for this supplier.");
        if (!await entitlements.Has(r.SupplierOrganisationId, Audience.Supplier, Feature, ct)) return Result<CampaignResponse>.Fail("plan.feature_not_included", "Sponsored placements are not part of your plan.");
        var now = clock.GetUtcNow();
        var created = SponsoredCampaign.Create(r.SupplierOrganisationId, r.Name, r.OfferId, r.BudgetCents, r.ShowingCents, r.Areas, r.StartsOn, r.EndsOn, DateOnly.FromDateTime(now.ToOffset(BusinessDay.Offset).Date), now);
        if (!created.IsSuccess) return Result<CampaignResponse>.Fail(created.Error!.Code, created.Error.Message);
        store.Add(created.Value!);
        await store.Save(ct);
        return Result<CampaignResponse>.Ok(Respond(created.Value!));
    }

    public async Task<Result<CampaignResponse>> Handle(PauseCampaign c, CancellationToken ct)
    {
        var campaign = await store.Campaign(c.CampaignId, ct);
        if (campaign is null) return Result<CampaignResponse>.Fail("campaign.not_found", "Campaign not found.");
        if (!await access.Has(c.PersonId, campaign.SupplierOrganisationId, "supplier.manage", ct)) return Result<CampaignResponse>.Fail("permission.denied", "You are not allowed to run campaigns for this supplier.");
        var paused = campaign.Pause(c.Paused);
        if (!paused.IsSuccess) return Result<CampaignResponse>.Fail(paused.Error!.Code, paused.Error.Message);
        await store.Save(ct);
        return Result<CampaignResponse>.Ok(Respond(campaign));
    }

    /// <summary>Counts showings, up to the budget, and records them as use that is billed. The reference makes a repeated report count once.</summary>
    public async Task<Result<CampaignUsageResponse>> Handle(RecordShowings c, CancellationToken ct)
    {
        var campaign = await store.Campaign(c.CampaignId, ct);
        if (campaign is null) return Result<CampaignUsageResponse>.Fail("campaign.not_found", "Campaign not found.");
        if (string.IsNullOrWhiteSpace(c.Reference)) return Result<CampaignUsageResponse>.Fail("usage.reference_required", "Usage needs a reference.");
        if (await store.UsageByReference(campaign.SupplierOrganisationId, $"campaign:{campaign.Id:N}:{c.Reference}", ct) is null)
        {
            var counted = campaign.Show(c.Showings);
            if (!counted.IsSuccess) { await store.Save(ct); return Result<CampaignUsageResponse>.Fail(counted.Error!.Code, counted.Error.Message); }
            var usage = UsageRecord.Create(campaign.SupplierOrganisationId, Metric, counted.Value!, $"campaign:{campaign.Id:N}:{c.Reference}", clock.GetUtcNow(), campaign.ShowingCents);
            store.Add(usage.Value!);
            await store.Save(ct);
        }
        return Result<CampaignUsageResponse>.Ok(new(campaign.Id, campaign.Showings, campaign.SpentCents, campaign.BudgetCents - campaign.SpentCents));
    }

    public async Task<Result<CampaignUsageResponse>> Handle(GetCampaignUsage q, CancellationToken ct)
    {
        var campaign = await store.Campaign(q.CampaignId, ct);
        if (campaign is null) return Result<CampaignUsageResponse>.Fail("campaign.not_found", "Campaign not found.");
        if (!await access.Has(q.PersonId, campaign.SupplierOrganisationId, "supplier.manage", ct)) return Result<CampaignUsageResponse>.Fail("permission.denied", "You are not allowed to see this campaign.");
        return Result<CampaignUsageResponse>.Ok(new(campaign.Id, campaign.Showings, campaign.SpentCents, campaign.BudgetCents - campaign.SpentCents));
    }

    public async Task<Result<List<CampaignResponse>>> Handle(ListCampaigns q, CancellationToken ct)
    {
        if (!await access.Has(q.PersonId, q.SupplierOrganisationId, "supplier.manage", ct)) return Result<List<CampaignResponse>>.Fail("permission.denied", "You are not allowed to see campaigns for this supplier.");
        return Result<List<CampaignResponse>>.Ok((await store.CampaignsOf(q.SupplierOrganisationId, ct)).OrderByDescending(x => x.CreatedAt).Select(Respond).ToList());
    }
}
