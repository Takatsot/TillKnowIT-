using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopCore.Application.Notifications;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.Dispatching;

namespace ShopCore.Infrastructure;

internal sealed class NotificationReadStore(ShopCoreDbContext db) : INotificationReadStore
{
    public Task<List<DateTimeOffset>> CancellationTimes(Guid shopId, DateTimeOffset since, CancellationToken ct) =>
        db.Set<SaleCancellation>().AsNoTracking().Where(c => c.ShopId == shopId && c.CancelledAt >= since).Select(c => c.CancelledAt).ToListAsync(ct);

    public async Task<Dictionary<string, string>> AlertSettings(Guid shopId, CancellationToken ct) =>
        await db.Set<ShopSetting>().AsNoTracking()
            .Where(s => s.ShopId == shopId && AlertSettings_Keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    private static readonly string[] AlertSettings_Keys = ShopCore.Domain.AlertSettings.Keys;
}

/// <summary>
/// Runs the owner's scheduled messages in the worker, every five minutes: compliance reminders, loss alerts,
/// offline-till alerts, and (after 19:00 in the shop) the daily summary. Every message has a dedupe key, so a
/// restart or a second worker can never send the same one twice.
/// </summary>
public sealed class OwnerNotificationService(IServiceScopeFactory scopes, ILogger<OwnerNotificationService> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A short pause at start-up so the host is fully up before the first run.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnce(stoppingToken);
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    internal async Task RunOnce(CancellationToken ct)
    {
        // Each job runs on its own, so one failing does not stop the others.
        await Run("compliance reminders", d => d.Send(new SendComplianceReminders(), ct), ct);
        await Run("loss alerts", d => d.Send(new EvaluateLossAlerts(), ct), ct);
        await Run("offline tills", d => d.Send(new AlertStaleDevices(), ct), ct);
        await Run("daily summaries", d => d.Send(new SendDailySummaries(), ct), ct);
    }

    private async Task Run(string name, Func<IDispatcher, Task<TillKnowIT.SharedKernel.Result<int>>> job, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await job(scope.ServiceProvider.GetRequiredService<IDispatcher>());
            if (result.IsSuccess && result.Value > 0) log.LogInformation("Queued {Count} {Job}", result.Value, name);
            if (!result.IsSuccess) log.LogWarning("{Job} failed: {Error}", name, result.Error?.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "{Job} failed", name);
        }
    }
}

public static class ShopCoreHostingExtensions
{
    /// <summary>Runs the owner's scheduled messages. Call this from the worker only.</summary>
    public static IServiceCollection AddOwnerNotifications(this IServiceCollection services) => services.AddHostedService<OwnerNotificationService>();
}
