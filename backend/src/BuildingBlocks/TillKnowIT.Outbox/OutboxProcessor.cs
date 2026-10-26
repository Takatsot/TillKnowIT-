using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TillKnowIT.Outbox;

/// <summary>
/// Publishes one module's outbox. Rows are claimed with FOR UPDATE SKIP LOCKED, so several workers can run
/// at once without handling the same event twice at the same time.
/// </summary>
public sealed class OutboxProcessor<TContext>(IServiceScopeFactory scopes, ILogger<OutboxProcessor<TContext>> log)
    : BackgroundService where TContext : DbContext
{
    public const int MaxAttempts = 10;
    private const int BatchSize = 50;
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                handled = await ProcessBatch(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Outbox batch failed for {Context}", typeof(TContext).Name);
            }

            if (handled == 0)
                await Task.Delay(Idle, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    public async Task<int> ProcessBatch(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var entity = db.Model.FindEntityType(typeof(OutboxMessage))!;
        var table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";

        await using var tx = await db.Database.BeginTransactionAsync(ct);
#pragma warning disable EF1003 // Table name comes from EF's own model and the limits are constants; identifiers cannot be parameters.
        var batch = await db.Set<OutboxMessage>()
            .FromSqlRaw($"SELECT * FROM {table} WHERE processed_at IS NULL AND attempts < {MaxAttempts} " +
                        $"ORDER BY occurred_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
#pragma warning restore EF1003

        foreach (var message in batch)
        {
            // Handlers get their own scope, so their DbContexts never share this transaction.
            await using var handlerScope = scopes.CreateAsyncScope();
            try
            {
                await OutboxEventDispatcher.Dispatch(message, handlerScope.ServiceProvider, ct);
                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.Error = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                log.LogWarning(ex, "Outbox message {Id} ({Type}) failed, attempt {Attempts}", message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }
}

public static class OutboxProcessorExtensions
{
    public static IServiceCollection AddOutboxProcessor<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddHostedService<OutboxProcessor<TContext>>();
        return services;
    }
}
