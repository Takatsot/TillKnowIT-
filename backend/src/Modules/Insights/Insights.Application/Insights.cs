using Billing.Contracts;
using Identity.Contracts;
using Insights.Contracts;
using Insights.Domain;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Insights.Application;

public interface IInsightsStore
{
    void Add(InsightOptIn optIn);
    Task<InsightOptIn?> OptIn(Guid shopId, CancellationToken ct);
    Task<List<Guid>> OptedInShops(CancellationToken ct);
    Task ReplaceDay(DateOnly day, IReadOnlyList<AreaAggregate> rows, CancellationToken ct);
    Task<List<AreaAggregate>> Rows(string area, string? category, DateOnly from, DateOnly to, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public static class OptInText
{
    public const string Explanation =
        "If you turn this on, we add your daily sales by product category to figures for your area. Nothing that names you, your staff or your customers is ever shared, " +
        "and a figure is only published when at least 10 shops are behind it. It is off until you turn it on, and you can change it at any time.";
}

public sealed record SetInsightOptIn(Guid ShopId, SetOptInRequest Request) : ICommand<OptInResponse>;
public sealed record GetInsightOptIn(Guid ShopId, Guid PersonId) : IQuery<OptInResponse>;
public sealed record GetInsightReport(InsightReportRequest Request) : IQuery<InsightReportResponse>;
public sealed record RunInsightAggregation(DateOnly? Day) : ICommand<int>;

internal sealed class InsightHandlers(IInsightsStore store, IOrganisationAccess access, IEntitlements entitlements, IShopDirectory shops, ICategorySalesSource source, TimeProvider clock)
    : ICommandHandler<SetInsightOptIn, OptInResponse>, IQueryHandler<GetInsightOptIn, OptInResponse>, IQueryHandler<GetInsightReport, InsightReportResponse>, ICommandHandler<RunInsightAggregation, int>
{
    public const int MaxReportDays = 366;

    private static OptInResponse Respond(Guid shopId, InsightOptIn? o) => new(shopId, o?.OptedIn ?? false, o?.ChangedAt, OptInText.Explanation);

    public async Task<Result<OptInResponse>> Handle(SetInsightOptIn c, CancellationToken ct)
    {
        if (!await access.Has(c.Request.PersonId, c.ShopId, "shop.settings", ct)) return Result<OptInResponse>.Fail("permission.denied", "Only the owner can change this.");
        var now = clock.GetUtcNow();
        var existing = await store.OptIn(c.ShopId, ct);
        if (existing is null)
        {
            existing = InsightOptIn.Create(c.ShopId, c.Request.OptedIn, c.Request.PersonId, now);
            store.Add(existing);
        }
        else existing.Set(c.Request.OptedIn, c.Request.PersonId, now);
        await store.Save(ct);
        return Result<OptInResponse>.Ok(Respond(c.ShopId, existing));
    }

    public async Task<Result<OptInResponse>> Handle(GetInsightOptIn q, CancellationToken ct)
    {
        if (!await access.Has(q.PersonId, q.ShopId, "shop.settings", ct)) return Result<OptInResponse>.Fail("permission.denied", "Only the owner can see this.");
        return Result<OptInResponse>.Ok(Respond(q.ShopId, await store.OptIn(q.ShopId, ct)));
    }

    /// <summary>
    /// G5-2: area trends by category, weekly. Every row comes from at least ten shops, and no row names a shop. The buyer needs the plan feature.
    /// </summary>
    public async Task<Result<InsightReportResponse>> Handle(GetInsightReport q, CancellationToken ct)
    {
        var r = q.Request;
        if (!Audience(r.Audience, out var permission)) return Result<InsightReportResponse>.Fail("insight.audience_invalid", "Audience must be shop or supplier.");
        if (!await access.Has(r.PersonId, r.OrganisationId, permission, ct)) return Result<InsightReportResponse>.Fail("permission.denied", "You are not allowed to buy reports for this business.");
        if (!await entitlements.Has(r.OrganisationId, r.Audience, PlanFeatures.InsightReports, ct)) return Result<InsightReportResponse>.Fail("plan.feature_not_included", "Market insights are not part of your plan.");
        if (string.IsNullOrWhiteSpace(r.Area)) return Result<InsightReportResponse>.Fail("insight.area_required", "Choose an area.");
        if (r.From > r.To || r.To.DayNumber - r.From.DayNumber + 1 > MaxReportDays) return Result<InsightReportResponse>.Fail("insight.range_invalid", $"Choose a period of up to {MaxReportDays} days.");

        var rows = (await store.Rows(r.Area.Trim().ToLowerInvariant(), r.Category?.Trim().ToLowerInvariant(), r.From, r.To, ct)).Where(x => Anonymity.CanPublish(x.ShopCount)).ToList();
        var trend = rows.GroupBy(x => (Week: WeekStart(x.Day), x.Category))
            .Select(g => new TrendRow(g.Key.Week, g.Key.Category, g.Sum(x => x.Units), Aggregator.RevenueBand(g.Sum(x => x.RevenueCents)), g.Min(x => x.ShopCount)))
            .OrderBy(t => t.WeekStart).ThenBy(t => t.Category).ToList();
        return Result<InsightReportResponse>.Ok(new(r.Area.Trim().ToLowerInvariant(), trend,
            trend.Count == 0 ? "There is not enough data for this area yet: we only publish figures from at least 10 shops." : "Each figure comes from at least 10 shops. No shop is named."));
    }

    private static bool Audience(string audience, out string permission)
    {
        permission = audience == "supplier" ? "supplier.manage" : "shop.settings";
        return audience is "shop" or "supplier";
    }

    private static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>
    /// Builds a day's figures from the shops that opted in: each shop's sales by category go into its area, and only areas and categories with ten or more shops are kept.
    /// Running it again for the same day replaces that day. Shops that opted out are not read at all.
    /// </summary>
    public async Task<Result<int>> Handle(RunInsightAggregation c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var day = c.Day ?? BusinessDay.Of(now).AddDays(-1);
        if (day >= BusinessDay.Of(now)) return Result<int>.Fail("insight.day_not_finished", "Only finished days are added.");

        var rows = new List<ShopDay>();
        foreach (var shopId in await store.OptedInShops(ct))
        {
            var shop = await shops.Get(shopId, ct);
            if (shop?.MunicipalityCode is not { Length: > 0 } area || shop.Status != "verified") continue;
            foreach (var cat in await source.Day(shopId, day, ct)) rows.Add(new(shopId, area, cat.CategoryCode, cat.Units, cat.RevenueCents));
        }
        var aggregates = Aggregator.Build(rows, day, now);
        await store.ReplaceDay(day, aggregates, ct);
        return Result<int>.Ok(aggregates.Count);
    }
}
