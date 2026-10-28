using Insights.Application;
using Insights.Domain;
using Insights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TillKnowIT.Dispatching;

namespace Insights.Infrastructure;

public static partial class InsightsModule
{
    static partial void AddServices(IServiceCollection services)
    {
        services.AddScoped<IInsightsStore, InsightsStore>();
        services.TryAddSingleton(TimeProvider.System);
    }

    /// <summary>Adds yesterday's figures every hour (re-running replaces the day). Call this from the worker only.</summary>
    public static IServiceCollection AddInsightAggregation(this IServiceCollection services) => services.AddHostedService<InsightAggregationService>();
}

internal sealed class InsightsStore(InsightsDbContext db) : IInsightsStore
{
    public void Add(InsightOptIn optIn) => db.Set<InsightOptIn>().Add(optIn);
    public Task<InsightOptIn?> OptIn(Guid shopId, CancellationToken ct) => db.Set<InsightOptIn>().FirstOrDefaultAsync(o => o.ShopId == shopId, ct);
    public Task<List<Guid>> OptedInShops(CancellationToken ct) => db.Set<InsightOptIn>().AsNoTracking().Where(o => o.OptedIn).Select(o => o.ShopId).ToListAsync(ct);

    public async Task ReplaceDay(DateOnly day, IReadOnlyList<AreaAggregate> rows, CancellationToken ct)
    {
        db.Set<AreaAggregate>().RemoveRange(await db.Set<AreaAggregate>().Where(a => a.Day == day).ToListAsync(ct));
        db.Set<AreaAggregate>().AddRange(rows);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<AreaAggregate>> Rows(string area, string? category, DateOnly from, DateOnly to, CancellationToken ct) =>
        db.Set<AreaAggregate>().AsNoTracking().Where(a => a.Area == area && a.Day >= from && a.Day <= to && (category == null || a.Category == category)).ToListAsync(ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class InsightAggregationService(IServiceScopeFactory scopes, ILogger<InsightAggregationService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new RunInsightAggregation(null), stoppingToken);
                if (result is { IsSuccess: true, Value: > 0 }) log.LogInformation("Insight aggregation published {Count} figures", result.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Insight aggregation failed");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
