using TillKnowIT.SharedKernel;

namespace Billing.Contracts;

public sealed record PlanFeatureDto(string Feature, long? Limit);

public sealed record PlanResponse(Guid Id, string Code, string Audience, string Name, long PriceCents, bool IsActive, List<PlanFeatureDto> Features);

public sealed record SavePlanRequest(Guid CallerPersonId, string Code, string Audience, string Name, long PriceCents, bool IsActive, List<PlanFeatureDto> Features);

public sealed record SubscribeRequest(Guid PersonId, Guid OrganisationId, string PlanCode);

public sealed record ChangeSubscriptionRequest(Guid PersonId, string? PlanCode, bool Cancel);

public sealed record SubscriptionResponse(Guid Id, Guid OrganisationId, string PlanCode, string Status, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd,
    string? PendingPlanCode, bool CancelAtPeriodEnd, Guid? InvoiceId, long ChargedCents);

public sealed record InvoiceLineResponse(string Description, string? Metric, long Quantity, long UnitCents, long AmountCents);

public sealed record InvoiceResponse(Guid Id, string Number, string Status, long SubtotalCents, int VatPercent, long VatCents, long TotalCents, DateTimeOffset DueAt, DateTimeOffset? PaidAt,
    List<InvoiceLineResponse> Lines);

public sealed record CreateCampaignRequest(Guid PersonId, Guid SupplierOrganisationId, string Name, Guid? OfferId, long BudgetCents, long ShowingCents, List<string> Areas, DateOnly StartsOn, DateOnly EndsOn);

/// <summary>Sponsored is always true: a paid placement is labelled as one wherever shops see it.</summary>
public sealed record CampaignResponse(Guid Id, string Name, string Status, long BudgetCents, long SpentCents, List<string> Areas, DateOnly StartsOn, DateOnly EndsOn, bool Sponsored);

public sealed record CampaignUsageResponse(Guid CampaignId, long Showings, long SpentCents, long RemainingCents);

/// <summary>What other modules ask before switching on a paid feature. Core selling never asks: it is not a plan feature and billing never blocks it.</summary>
public interface IEntitlements
{
    /// <summary>Whether the organisation's plan includes the feature right now. A lapsed or cancelled subscription falls back to the audience's free plan.</summary>
    Task<bool> Has(Guid organisationId, string audience, string feature, CancellationToken ct);

    /// <summary>The limit for a feature: null if there is none or the feature is not on the plan (check Has first).</summary>
    Task<long?> LimitFor(Guid organisationId, string audience, string feature, CancellationToken ct);
}

/// <summary>How other modules report something billed by use. Safe to repeat with the same reference.</summary>
public interface IUsageMeter
{
    Task<Result> Record(Guid organisationId, string metric, long quantity, string reference, DateTimeOffset occurredAt, CancellationToken ct);
}

/// <summary>The feature codes plans switch on. Core selling is not on this list: no plan, paid or not, can switch the till off.</summary>
public static class PlanFeatures
{
    public const string Messages = "messages";
    public const string MultiBranch = "multi_branch";
    public const string AccountingExport = "accounting_export";
    public const string InsightReports = "insight_reports";
    public const string PayLater = "pay_later";
    public const string PartnerApi = "partner_api";
    public const string Sponsored = "sponsored";
    public const string SupplierAnalytics = "supplier_analytics";
}
