namespace Insights.Contracts;

public sealed record SetOptInRequest(Guid PersonId, bool OptedIn);

public sealed record OptInResponse(Guid ShopId, bool OptedIn, DateTimeOffset? ChangedAt, string Explanation);

/// <summary>Audience is shop or supplier: it decides which plan and which permission the buyer needs.</summary>
public sealed record InsightReportRequest(Guid PersonId, Guid OrganisationId, string Audience, string Area, string? Category, DateOnly From, DateOnly To);

public sealed record TrendRow(DateOnly WeekStart, string Category, int Units, string RevenueBand, int Shops);

public sealed record InsightReportResponse(string Area, List<TrendRow> Rows, string Note);
