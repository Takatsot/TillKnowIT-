using TillKnowIT.SharedKernel;

namespace Billing.Domain;

public static class Audience
{
    public const string Shop = "shop";
    public const string Supplier = "supplier";
    public static bool IsValid(string? a) => a is Shop or Supplier;
}

public enum SubscriptionStatus { Active, PastDue, Limited, Cancelled }

public enum InvoiceStatus { Open, Paid, Failed, Void }

/// <summary>The numbers the owner of the platform can change without a release. Prices of plans are data (see <see cref="Plan"/>).</summary>
public sealed record BillingSettings(
    int VatPercent = 15,
    int GraceDays = 7,
    int[]? ReminderDays = null,
    bool DowngradeAtPeriodEnd = true,
    int InvoiceDueDays = 7,
    Dictionary<string, long>? UnitPrices = null)
{
    public int[] Reminders => ReminderDays ?? [0, 3, 6];

    /// <summary>What one unit of a metric costs once the plan's included amount is used up (cents before VAT).</summary>
    public long UnitPrice(string metric) => (UnitPrices ?? Defaults).GetValueOrDefault(metric);

    private static readonly Dictionary<string, long> Defaults = new() { ["messages"] = 40 };
}

/// <summary>Money maths for invoices. Everything is in cents and rounds half-up, so the invoice always equals the usage it bills.</summary>
public static class BillingMath
{
    public static long Vat(long subtotalCents, int percent) => (subtotalCents * percent + 50) / 100;

    /// <summary>Part of a price for the time left in a period.</summary>
    public static long ProRata(long priceCents, DateTimeOffset from, DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        var total = (periodEnd - periodStart).TotalDays;
        if (total <= 0) return 0;
        var left = Math.Clamp((periodEnd - from).TotalDays, 0, total);
        return (long)Math.Floor(priceCents * left / total + 0.5);
    }

    /// <summary>What an upgrade costs now: the new plan for the rest of the period, less what is left of the old one. Never negative: a downgrade does not refund.</summary>
    public static long UpgradeCharge(long oldPriceCents, long newPriceCents, DateTimeOffset now, DateTimeOffset periodStart, DateTimeOffset periodEnd) =>
        Math.Max(0, ProRata(newPriceCents, now, periodStart, periodEnd) - ProRata(oldPriceCents, now, periodStart, periodEnd));

    /// <summary>Usage above what the plan includes, billed at the unit price. Included units are free; a missing limit means nothing is included.</summary>
    public static (long BillableQuantity, long AmountCents) Overage(long used, long? included, long unitCents)
    {
        var billable = Math.Max(0, used - Math.Max(0, included ?? 0));
        return (billable, billable * unitCents);
    }
}

/// <summary>G3-1: a plan for shops or suppliers. Price, features and limits are data, so a plan can change without a release.</summary>
public sealed class Plan : VersionedEntity
{
    private Plan() { }

    public string Code { get; private set; } = default!;
    public string Audience { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public long PriceCents { get; private set; }
    public bool IsActive { get; private set; } = true;

    public bool IsFree => PriceCents == 0;

    public static Result<Plan> Create(string? code, string? audience, string? name, long priceCents, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 40 || code.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')))
            return Result<Plan>.Fail("plan.code_invalid", "A plan code is lowercase letters, digits and underscores.");
        if (!Domain.Audience.IsValid(audience)) return Result<Plan>.Fail("plan.audience_invalid", "A plan is for shops or suppliers.");
        if (string.IsNullOrWhiteSpace(name)) return Result<Plan>.Fail("plan.name_invalid", "A plan needs a name.");
        if (priceCents < 0) return Result<Plan>.Fail("plan.price_invalid", "A price cannot be negative.");
        var p = new Plan { Code = code, Audience = audience!, Name = name.Trim(), PriceCents = priceCents };
        p.StampFromDevice(Guid.CreateVersion7(), now, null, null);
        return Result<Plan>.Ok(p);
    }

    public static Plan Seeded(Guid id, string code, string audience, string name, long priceCents, DateTimeOffset at)
    {
        var p = new Plan { Code = code, Audience = audience, Name = name, PriceCents = priceCents };
        p.StampFromDevice(id, at, null, null);
        return p;
    }

    public Result Update(string? name, long priceCents, bool active)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result.Fail("plan.name_invalid", "A plan needs a name.");
        if (priceCents < 0) return Result.Fail("plan.price_invalid", "A price cannot be negative.");
        Name = name.Trim();
        PriceCents = priceCents;
        IsActive = active;
        Touch();
        return Result.Ok();
    }
}

/// <summary>One feature a plan switches on. Limit is how many (for example messages a month); null means no limit.</summary>
public sealed class PlanFeature : Entity
{
    private PlanFeature() { }

    public Guid PlanId { get; private set; }
    public string Feature { get; private set; } = default!;
    public long? Limit { get; private set; }

    public PlanFeature(Guid planId, string feature, long? limit, DateTimeOffset now)
    {
        PlanId = planId;
        Feature = feature;
        Limit = limit;
        CreatedAt = now;
    }

    public static PlanFeature Seeded(Guid id, Guid planId, string feature, long? limit, DateTimeOffset now) =>
        new(planId, feature, limit, now) { Id = id };
}

/// <summary>
/// G3-2, G3-4: an organisation's subscription. If a payment fails it goes PastDue, then Limited after the grace period: only the plan's extra features
/// are switched off, and the till is never touched. Paying brings it back at once.
/// </summary>
public sealed class Subscription : VersionedEntity
{
    private Subscription() { }

    public Guid OrganisationId { get; private set; }
    public Guid PlanId { get; private set; }
    public string PlanCode { get; private set; } = default!;
    public long PriceCents { get; private set; }
    public Guid BillingPersonId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset PeriodStart { get; private set; }
    public DateTimeOffset PeriodEnd { get; private set; }
    public Guid? PendingPlanId { get; private set; }
    public bool CancelAtPeriodEnd { get; private set; }
    public DateTimeOffset? PastDueSince { get; private set; }
    public int RemindersSent { get; private set; }

    public bool GivesPaidFeatures => Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;

    public static Subscription Start(Guid organisationId, Plan plan, Guid personId, DateTimeOffset now)
    {
        var s = new Subscription { OrganisationId = organisationId, BillingPersonId = personId };
        s.StampFromDevice(Guid.CreateVersion7(), now, personId, null);
        s.Begin(plan, now);
        return s;
    }

    private void Begin(Plan plan, DateTimeOffset now)
    {
        PlanId = plan.Id;
        PlanCode = plan.Code;
        PriceCents = plan.PriceCents;
        Status = SubscriptionStatus.Active;
        PeriodStart = now;
        PeriodEnd = now.AddMonths(1);
        PendingPlanId = null;
        CancelAtPeriodEnd = false;
        PastDueSince = null;
        RemindersSent = 0;
    }

    /// <summary>Starts again after a cancellation.</summary>
    public Result Restart(Plan plan, Guid personId, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Cancelled) return Result.Fail("subscription.already_active", "This subscription is still running.");
        Begin(plan, now);
        BillingPersonId = personId;
        Touch();
        return Result.Ok();
    }

    /// <summary>Moves to another plan now (the caller bills the pro-rata difference for a dearer one) or at the end of the period.</summary>
    public Result Change(Plan plan, bool atPeriodEnd)
    {
        if (Status == SubscriptionStatus.Cancelled) return Result.Fail("subscription.cancelled", "This subscription was cancelled.");
        if (plan.Id == PlanId && PendingPlanId is null && !CancelAtPeriodEnd) return Result.Fail("subscription.same_plan", "That is already the plan.");
        if (atPeriodEnd) PendingPlanId = plan.Id == PlanId ? null : plan.Id;
        else
        {
            PlanId = plan.Id;
            PlanCode = plan.Code;
            PriceCents = plan.PriceCents;
            PendingPlanId = null;
        }
        CancelAtPeriodEnd = false;
        Touch();
        return Result.Ok();
    }

    public Result Cancel()
    {
        if (Status == SubscriptionStatus.Cancelled) return Result.Fail("subscription.cancelled", "This subscription was cancelled.");
        CancelAtPeriodEnd = true;
        PendingPlanId = null;
        Touch();
        return Result.Ok();
    }

    public bool IsDue(DateTimeOffset now) => Status != SubscriptionStatus.Cancelled && PeriodEnd <= now;

    /// <summary>Rolls into the next period, taking up a pending plan. Returns false if the subscription ended instead.</summary>
    public bool Renew(Plan? pending)
    {
        if (CancelAtPeriodEnd)
        {
            Status = SubscriptionStatus.Cancelled;
            Touch();
            return false;
        }
        if (pending is not null)
        {
            PlanId = pending.Id;
            PlanCode = pending.Code;
            PriceCents = pending.PriceCents;
            PendingPlanId = null;
        }
        PeriodStart = PeriodEnd;
        PeriodEnd = PeriodStart.AddMonths(1);
        Touch();
        return true;
    }

    public void PaymentFailed(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Active) return;
        Status = SubscriptionStatus.PastDue;
        PastDueSince = now;
        RemindersSent = 0;
        Touch();
    }

    public void PaymentReceived()
    {
        if (Status is not (SubscriptionStatus.PastDue or SubscriptionStatus.Limited)) return;
        Status = SubscriptionStatus.Active;
        PastDueSince = null;
        RemindersSent = 0;
        Touch();
    }

    /// <summary>The reminder number now owed (reminders go on the days in settings, the first the day it failed), or none; and whether the grace period just ended.</summary>
    public (int? Reminder, bool NowLimited) Dunning(DateTimeOffset now, BillingSettings settings)
    {
        if (Status != SubscriptionStatus.PastDue || PastDueSince is not { } since) return (null, false);
        var days = (int)Math.Floor((now - since).TotalDays);
        if (days >= settings.GraceDays)
        {
            Status = SubscriptionStatus.Limited;
            Touch();
            return (null, true);
        }
        var due = settings.Reminders.Count(d => d <= days);
        if (due <= RemindersSent) return (null, false);
        RemindersSent = due;
        return (due, false);
    }
}

/// <summary>An amount owed, with its lines. Prices are before VAT; the invoice adds VAT once.</summary>
public sealed class Invoice : Entity
{
    private Invoice() { }

    public string Number { get; private set; } = default!;
    public string Reference { get; private set; } = default!;
    public Guid OrganisationId { get; private set; }
    public Guid? SubscriptionId { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public long SubtotalCents { get; private set; }
    public int VatPercent { get; private set; }
    public long VatCents { get; private set; }
    public long TotalCents { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public int Attempts { get; private set; }

    public static Invoice Issue(string number, string reference, Guid organisationId, Guid? subscriptionId, IReadOnlyList<InvoiceLine> lines, BillingSettings settings, DateTimeOffset now)
    {
        var subtotal = lines.Sum(l => l.AmountCents);
        var vat = BillingMath.Vat(subtotal, settings.VatPercent);
        var inv = new Invoice
        {
            Number = number, Reference = reference, OrganisationId = organisationId, SubscriptionId = subscriptionId, Status = InvoiceStatus.Open,
            SubtotalCents = subtotal, VatPercent = settings.VatPercent, VatCents = vat, TotalCents = subtotal + vat, DueAt = now.AddDays(settings.InvoiceDueDays),
        };
        inv.StampFromDevice(Guid.CreateVersion7(), now, null, null);
        foreach (var l in lines) l.Attach(inv.Id);
        return inv;
    }

    public void MarkPaid(DateTimeOffset now)
    {
        Attempts++;
        Status = InvoiceStatus.Paid;
        PaidAt = now;
    }

    public void MarkFailed()
    {
        Attempts++;
        Status = InvoiceStatus.Failed;
    }
}

public sealed class InvoiceLine : Entity
{
    private InvoiceLine() { }

    public Guid InvoiceId { get; private set; }
    public string Description { get; private set; } = default!;
    public string? Metric { get; private set; }
    public long Quantity { get; private set; }
    public long UnitCents { get; private set; }
    public long AmountCents { get; private set; }

    public InvoiceLine(string description, string? metric, long quantity, long unitCents, DateTimeOffset now)
    {
        Description = description;
        Metric = metric;
        Quantity = quantity;
        UnitCents = unitCents;
        AmountCents = quantity * unitCents;
        CreatedAt = now;
    }

    internal void Attach(Guid invoiceId) => InvoiceId = invoiceId;
}

/// <summary>A counted use of something billed by use (messages, sponsored showings). The reference makes recording it safe to repeat.</summary>
public sealed class UsageRecord : Entity
{
    private UsageRecord() { }

    public Guid OrganisationId { get; private set; }
    public string Metric { get; private set; } = default!;
    public long Quantity { get; private set; }
    public string Reference { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? InvoiceId { get; private set; }

    /// <summary>Set when the use has its own price (a sponsored showing): it is billed in full at this price. Without it the plan decides what is included and what is charged for.</summary>
    public long? UnitCents { get; private set; }

    public static Result<UsageRecord> Create(Guid organisationId, string? metric, long quantity, string? reference, DateTimeOffset occurredAt, long? unitCents = null)
    {
        if (string.IsNullOrWhiteSpace(metric)) return Result<UsageRecord>.Fail("usage.metric_invalid", "Usage needs a metric.");
        if (quantity <= 0) return Result<UsageRecord>.Fail("usage.quantity_invalid", "Usage counts one or more.");
        if (string.IsNullOrWhiteSpace(reference)) return Result<UsageRecord>.Fail("usage.reference_required", "Usage needs a reference.");
        var u = new UsageRecord { OrganisationId = organisationId, Metric = metric, Quantity = quantity, Reference = reference, OccurredAt = occurredAt, UnitCents = unitCents };
        u.StampFromDevice(Guid.CreateVersion7(), occurredAt, null, null);
        return Result<UsageRecord>.Ok(u);
    }

    public void Bill(Guid invoiceId) => InvoiceId = invoiceId;
}

public enum CampaignStatus { Active, Paused, Ended }

/// <summary>G3-3: a supplier's paid promotion: a budget, the areas and the dates. Billed by use and always labelled "Sponsored" to shops.</summary>
public sealed class SponsoredCampaign : VersionedEntity
{
    private SponsoredCampaign() { }

    public Guid SupplierOrganisationId { get; private set; }
    public string Name { get; private set; } = default!;
    public Guid? OfferId { get; private set; }
    public long BudgetCents { get; private set; }
    public long SpentCents { get; private set; }
    public long ShowingCents { get; private set; }
    public long Showings { get; private set; }
    public string Areas { get; private set; } = "";
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public CampaignStatus Status { get; private set; }

    public IReadOnlyList<string> AreaList => Areas.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static Result<SponsoredCampaign> Create(Guid supplierOrganisationId, string? name, Guid? offerId, long budgetCents, long showingCents, IEnumerable<string>? areas,
        DateOnly startsOn, DateOnly endsOn, DateOnly today, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result<SponsoredCampaign>.Fail("campaign.name_invalid", "A campaign needs a name.");
        if (budgetCents <= 0) return Result<SponsoredCampaign>.Fail("campaign.budget_invalid", "A campaign needs a budget.");
        if (showingCents <= 0 || showingCents > budgetCents) return Result<SponsoredCampaign>.Fail("campaign.price_invalid", "A campaign needs a price per showing that the budget can pay for.");
        var list = (areas ?? []).Select(a => a.Trim().ToLowerInvariant()).Where(a => a.Length > 0).Distinct().Order().ToList();
        if (list.Count == 0) return Result<SponsoredCampaign>.Fail("campaign.areas_required", "Choose at least one area.");
        if (endsOn < startsOn || endsOn < today) return Result<SponsoredCampaign>.Fail("campaign.dates_invalid", "The end date cannot be before the start date or in the past.");
        var c = new SponsoredCampaign
        {
            SupplierOrganisationId = supplierOrganisationId, Name = name.Trim(), OfferId = offerId, BudgetCents = budgetCents, ShowingCents = showingCents,
            Areas = string.Join(',', list), StartsOn = startsOn, EndsOn = endsOn, Status = CampaignStatus.Active,
        };
        c.StampFromDevice(Guid.CreateVersion7(), now, null, null);
        return Result<SponsoredCampaign>.Ok(c);
    }

    public bool RunsOn(DateOnly day, string area) =>
        Status == CampaignStatus.Active && day >= StartsOn && day <= EndsOn && SpentCents + ShowingCents <= BudgetCents && AreaList.Contains(area.Trim().ToLowerInvariant());

    /// <summary>Counts showings and what they cost, and returns how many were counted. Spending stops at the budget: a campaign never bills more than it was given.</summary>
    public Result<long> Show(long showings)
    {
        if (showings <= 0) return Result<long>.Fail("campaign.showings_invalid", "Count one or more showings.");
        if (Status != CampaignStatus.Active) return Result<long>.Fail("campaign.not_running", "This campaign is not running.");
        var affordable = Math.Min(showings, (BudgetCents - SpentCents) / ShowingCents);
        if (affordable <= 0)
        {
            Status = CampaignStatus.Ended;
            return Result<long>.Fail("campaign.budget_used", "The budget is used up.");
        }
        SpentCents += affordable * ShowingCents;
        Showings += affordable;
        if (SpentCents + ShowingCents > BudgetCents) Status = CampaignStatus.Ended;
        Touch();
        return Result<long>.Ok(affordable);
    }

    public Result Pause(bool paused)
    {
        if (Status == CampaignStatus.Ended) return Result.Fail("campaign.ended", "This campaign has ended.");
        Status = paused ? CampaignStatus.Paused : CampaignStatus.Active;
        Touch();
        return Result.Ok();
    }
}
