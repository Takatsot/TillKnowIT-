using Accounting.Application;
using Accounting.Contracts;
using Accounting.Domain;
using Accounting.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Accounting.Infrastructure;

public static partial class AccountingModule
{
    static partial void AddServices(IServiceCollection services)
    {
        services.AddScoped<IAccountingStore, AccountingStore>();
        services.TryAddSingleton(TimeProvider.System);
    }

    /// <summary>Sends each finished day to the connected package every hour. Call this from the worker only.</summary>
    public static IServiceCollection AddAccountingExports(this IServiceCollection services) => services.AddHostedService<AccountingExportService>();

    /// <summary>Development only: pretend Xero and Sage that accept every journal and remember what they were sent. The real adapters need each package's developer account.</summary>
    public static IServiceCollection AddDevAccountingExporters(this IServiceCollection services)
    {
        services.AddSingleton<IAccountingExporter>(new DevAccountingExporter(Providers.Xero));
        services.AddSingleton<IAccountingExporter>(new DevAccountingExporter(Providers.Sage));
        return services;
    }
}

/// <summary>Posts nowhere. Repeating a key returns the first reference, as a real package would be asked to.</summary>
public sealed class DevAccountingExporter(string provider) : IAccountingExporter
{
    private readonly Dictionary<string, string> _posted = [];

    public string Provider => provider;
    public IReadOnlyDictionary<string, string> Posted => _posted;

    public Task<Result<ExchangeResult>> Exchange(string authorisationCode, CancellationToken ct) =>
        Task.FromResult(string.IsNullOrWhiteSpace(authorisationCode) ? Result<ExchangeResult>.Fail("accounting.code_invalid", "No code.") : Result<ExchangeResult>.Ok(new($"dev-token-{provider}", $"Dev {provider} organisation")));

    public Task<Result<string>> Post(string tokenReference, Journal journal, string idempotencyKey, CancellationToken ct)
    {
        lock (_posted)
        {
            if (!_posted.TryGetValue(idempotencyKey, out var reference)) _posted[idempotencyKey] = reference = $"{provider}-journal-{_posted.Count + 1}";
            return Task.FromResult(Result<string>.Ok(reference));
        }
    }
}

internal sealed class AccountingStore(AccountingDbContext db) : IAccountingStore
{
    public void Add(AccountingConnection connection) => db.Set<AccountingConnection>().Add(connection);
    public Task<AccountingConnection?> Connection(Guid shopId, CancellationToken ct) => db.Set<AccountingConnection>().FirstOrDefaultAsync(c => c.ShopId == shopId, ct);
    public Task<List<AccountingConnection>> ActiveConnections(CancellationToken ct) => db.Set<AccountingConnection>().AsNoTracking().Where(c => c.DisconnectedAt == null).ToListAsync(ct);
    public void Add(AccountMapping mapping) => db.Set<AccountMapping>().Add(mapping);
    public Task<List<AccountMapping>> Mappings(Guid shopId, CancellationToken ct) => db.Set<AccountMapping>().Where(m => m.ShopId == shopId).ToListAsync(ct);
    public void Add(ExportRun run) => db.Set<ExportRun>().Add(run);
    public Task<ExportRun?> Run(Guid id, CancellationToken ct) => db.Set<ExportRun>().FirstOrDefaultAsync(r => r.Id == id, ct);
    public Task<ExportRun?> RunFor(Guid shopId, string provider, DateOnly day, CancellationToken ct) => db.Set<ExportRun>().FirstOrDefaultAsync(r => r.ShopId == shopId && r.Provider == provider && r.Day == day, ct);
    public Task<List<ExportRun>> Runs(Guid shopId, int take, CancellationToken ct) => db.Set<ExportRun>().AsNoTracking().Where(r => r.ShopId == shopId).OrderByDescending(r => r.Day).Take(take).ToListAsync(ct);
    public Task<List<ExportRun>> RunsSince(Guid shopId, string provider, DateOnly from, CancellationToken ct) => db.Set<ExportRun>().Where(r => r.ShopId == shopId && r.Provider == provider && r.Day >= from).ToListAsync(ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class AccountingExportService(IServiceScopeFactory scopes, ILogger<AccountingExportService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new RunAccountingExports(), stoppingToken);
                if (result is { IsSuccess: true, Value: > 0 }) log.LogInformation("Sent {Count} accounting journals", result.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Accounting export run failed");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

public static class AccountingEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists.
    public static IEndpointRouteBuilder MapAccountingEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/shops/{shopId:guid}/accounting/connections/{provider}", async (Guid shopId, string provider, ConnectRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ConnectAccounting(shopId, provider, req), ct)));
        api.MapDelete("/shops/{shopId:guid}/accounting/connection", async (Guid shopId, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new DisconnectAccounting(shopId, personId), ct)));
        api.MapPut("/shops/{shopId:guid}/accounting/mappings", async (Guid shopId, SaveMappingsRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SaveAccountMappings(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/accounting/mappings", async (Guid shopId, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetAccountMappings(shopId, personId), ct)));
        api.MapGet("/shops/{shopId:guid}/accounting/export-runs", async (Guid shopId, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListExportRuns(shopId, personId), ct)));
        api.MapPost("/shops/{shopId:guid}/accounting/export-runs/{runId:guid}/retry", async (Guid shopId, Guid runId, RetryRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RetryExport(shopId, runId, req), ct)));
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(title: result.Error!.Message,
        extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code },
        statusCode: result.Error.Code switch
        {
            "permission.denied" or "plan.feature_not_included" => 403,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) => 404,
            "accounting.already_connected" or "accounting.not_connected" or "run.already_sent" => 409,
            _ => 422,
        });
}
