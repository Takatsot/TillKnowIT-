using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopCore.Application.Selling;
using TillKnowIT.Dispatching;

namespace ShopCore.Infrastructure;

/// <summary>Once an hour: a lay-by past its due date that is not paid off lapses on the shop's cancellation terms. Call this from the worker only.</summary>
public sealed class LaybyExpiryService(IServiceScopeFactory scopes, ILogger<LaybyExpiryService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new ExpireDueLaybys(), stoppingToken);
                if (result is { IsSuccess: true, Value: > 0 }) log.LogInformation("{Count} lay-bys expired", result.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Expiring lay-bys failed");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

public static class ShopCoreSellingHosting
{
    /// <summary>Expires overdue lay-bys. Call this from the worker only.</summary>
    public static IServiceCollection AddLaybyExpiry(this IServiceCollection services) => services.AddHostedService<LaybyExpiryService>();
}
