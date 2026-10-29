using Accounting.Contracts;
using Accounting.Domain;
using Billing.Contracts;
using Identity.Contracts;
using Messaging.Contracts;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Accounting.Application;

public sealed record ExchangeResult(string TokenReference, string? TenantName);

/// <summary>One accounting package. Adding a package is adding one of these: nothing else changes.</summary>
public interface IAccountingExporter
{
    string Provider { get; }

    /// <summary>Swaps the code from the package's sign-in screen for access, kept in the secrets store; we keep only the reference.</summary>
    Task<Result<ExchangeResult>> Exchange(string authorisationCode, CancellationToken ct);

    /// <summary>Posts a day's journal. The key makes a repeat safe: the package must not post the same key twice. Returns the package's own reference.</summary>
    Task<Result<string>> Post(string tokenReference, Journal journal, string idempotencyKey, CancellationToken ct);
}

public interface IAccountingStore
{
    void Add(AccountingConnection connection);
    Task<AccountingConnection?> Connection(Guid shopId, CancellationToken ct);
    Task<List<AccountingConnection>> ActiveConnections(CancellationToken ct);
    void Add(AccountMapping mapping);
    Task<List<AccountMapping>> Mappings(Guid shopId, CancellationToken ct);
    void Add(ExportRun run);
    Task<ExportRun?> Run(Guid id, CancellationToken ct);
    Task<ExportRun?> RunFor(Guid shopId, string provider, DateOnly day, CancellationToken ct);
    Task<List<ExportRun>> Runs(Guid shopId, int take, CancellationToken ct);
    Task<List<ExportRun>> RunsSince(Guid shopId, string provider, DateOnly from, CancellationToken ct);
    Task Save(CancellationToken ct);
}

internal static class AccountingMapping
{
    public static ConnectionResponse ToResponse(AccountingConnection c) => new(c.Id, c.Provider, c.TenantName, c.IsConnected, c.StartsOn, c.ConnectedAt);
    public static ExportRunResponse ToResponse(ExportRun r) => new(r.Id, r.Provider, r.Day, r.Status.ToString().ToLowerInvariant(), r.Attempts, r.LastError, r.ExternalReference, r.GivenUp);
}

public sealed record ConnectAccounting(Guid ShopId, string Provider, ConnectRequest Request) : ICommand<ConnectionResponse>;
public sealed record GetAccountingConnection(Guid ShopId, Guid PersonId) : IQuery<ConnectionResponse>;
public sealed record DisconnectAccounting(Guid ShopId, Guid PersonId) : ICommand<ConnectionResponse>;
public sealed record SaveAccountMappings(Guid ShopId, SaveMappingsRequest Request) : ICommand<MappingsResponse>;
public sealed record GetAccountMappings(Guid ShopId, Guid PersonId) : IQuery<MappingsResponse>;
public sealed record ListExportRuns(Guid ShopId, Guid PersonId) : IQuery<List<ExportRunResponse>>;
public sealed record RetryExport(Guid ShopId, Guid RunId, RetryRequest Request) : ICommand<ExportRunResponse>;
public sealed record RunAccountingExports : ICommand<int>;

internal sealed class AccountingHandlers(IAccountingStore store, IEnumerable<IAccountingExporter> exporters, IOrganisationAccess access, IEntitlements entitlements, IBranchDirectory branches,
    IDailyAccountingSource source, IShopDirectory shops, IMessageQueue queue, TimeProvider clock)
    : ICommandHandler<ConnectAccounting, ConnectionResponse>, ICommandHandler<DisconnectAccounting, ConnectionResponse>, ICommandHandler<SaveAccountMappings, MappingsResponse>,
      IQueryHandler<GetAccountMappings, MappingsResponse>, IQueryHandler<GetAccountingConnection, ConnectionResponse>, IQueryHandler<ListExportRuns, List<ExportRunResponse>>, ICommandHandler<RetryExport, ExportRunResponse>, ICommandHandler<RunAccountingExports, int>
{
    public const int CatchUpDays = 7;

    private IAccountingExporter? Exporter(string? provider) => exporters.FirstOrDefault(e => e.Provider.Equals(provider?.Trim(), StringComparison.OrdinalIgnoreCase));

    private Task<bool> MayManage(Guid personId, Guid shopId, CancellationToken ct) => access.Has(personId, shopId, "shop.settings", ct);

    private async Task<bool> HasPlan(Guid shopId, CancellationToken ct) => await entitlements.Has(await branches.HeadOf(shopId, ct), "shop", PlanFeatures.AccountingExport, ct);

    private async Task<MappingsResponse> MappingsOf(Guid shopId, CancellationToken ct)
    {
        var rows = await store.Mappings(shopId, ct);
        return new(rows.OrderBy(m => m.Key).Select(m => new MappingEntry(m.Key, m.AccountCode)).ToList(), AccountKeys.All.Where(k => rows.All(m => m.Key != k)).ToList());
    }

    public async Task<Result<ConnectionResponse>> Handle(ConnectAccounting c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await MayManage(r.PersonId, c.ShopId, ct)) return Result<ConnectionResponse>.Fail("permission.denied", "Only the owner can connect an accounting package.");
        if (!await HasPlan(c.ShopId, ct)) return Result<ConnectionResponse>.Fail("plan.feature_not_included", "Accounting export is not part of your plan.");
        var exporter = Exporter(c.Provider);
        if (exporter is null) return Result<ConnectionResponse>.Fail("accounting.provider_unknown", "Choose Xero or Sage.");

        var access_ = await exporter.Exchange(r.AuthorisationCode, ct);
        if (!access_.IsSuccess) return Result<ConnectionResponse>.Fail("accounting.connect_failed", "We could not connect to the package. Sign in again.");
        var now = clock.GetUtcNow();
        var startsOn = r.StartsOn ?? BusinessDay.Of(now);   // by default only days from now on: past books are the accountant's

        var existing = await store.Connection(c.ShopId, ct);
        if (existing is null)
        {
            var created = AccountingConnection.Connect(c.ShopId, exporter.Provider, access_.Value!.TokenReference, access_.Value.TenantName, r.PersonId, startsOn, now);
            if (!created.IsSuccess) return Result<ConnectionResponse>.Fail(created.Error!.Code, created.Error.Message);
            store.Add(created.Value!);
            existing = created.Value!;
        }
        else if (existing.Provider != exporter.Provider && existing.IsConnected) return Result<ConnectionResponse>.Fail("accounting.already_connected", "Disconnect the current package before connecting another.");
        else existing.Reconnect(exporter.Provider, access_.Value!.TokenReference, access_.Value.TenantName, startsOn, now);
        await store.Save(ct);
        return Result<ConnectionResponse>.Ok(AccountingMapping.ToResponse(existing));
    }

    public async Task<Result<ConnectionResponse>> Handle(GetAccountingConnection q, CancellationToken ct)
    {
        if (!await MayManage(q.PersonId, q.ShopId, ct)) return Result<ConnectionResponse>.Fail("permission.denied", "Only the owner can see this.");
        var connection = await store.Connection(q.ShopId, ct);
        return connection is null ? Result<ConnectionResponse>.Fail("accounting.not_connected", "No accounting package is connected.") : Result<ConnectionResponse>.Ok(AccountingMapping.ToResponse(connection));
    }

    public async Task<Result<ConnectionResponse>> Handle(DisconnectAccounting c, CancellationToken ct)
    {
        if (!await MayManage(c.PersonId, c.ShopId, ct)) return Result<ConnectionResponse>.Fail("permission.denied", "Only the owner can disconnect.");
        var connection = await store.Connection(c.ShopId, ct);
        if (connection is null || !connection.IsConnected) return Result<ConnectionResponse>.Fail("accounting.not_connected", "No accounting package is connected.");
        connection.Disconnect(clock.GetUtcNow());
        await store.Save(ct);
        return Result<ConnectionResponse>.Ok(AccountingMapping.ToResponse(connection));
    }

    public async Task<Result<MappingsResponse>> Handle(SaveAccountMappings c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await MayManage(r.PersonId, c.ShopId, ct)) return Result<MappingsResponse>.Fail("permission.denied", "Only the owner can map accounts.");
        var existing = await store.Mappings(c.ShopId, ct);
        var now = clock.GetUtcNow();
        foreach (var entry in r.Mappings ?? [])
        {
            var row = existing.FirstOrDefault(m => m.Key == entry.Key);
            if (row is null)
            {
                var created = AccountMapping.Create(c.ShopId, entry.Key, entry.AccountCode, now);
                if (!created.IsSuccess) return Result<MappingsResponse>.Fail(created.Error!.Code, created.Error.Message);
                store.Add(created.Value!);
                existing.Add(created.Value!);
            }
            else if (string.IsNullOrWhiteSpace(entry.AccountCode) || entry.AccountCode.Length > 40) return Result<MappingsResponse>.Fail("mapping.account_invalid", "Give the account code from your books.");
            else row.SetAccount(entry.AccountCode);
        }
        await store.Save(ct);
        return Result<MappingsResponse>.Ok(await MappingsOf(c.ShopId, ct));
    }

    public async Task<Result<MappingsResponse>> Handle(GetAccountMappings q, CancellationToken ct) =>
        await MayManage(q.PersonId, q.ShopId, ct) ? Result<MappingsResponse>.Ok(await MappingsOf(q.ShopId, ct)) : Result<MappingsResponse>.Fail("permission.denied", "Only the owner can see this.");

    public async Task<Result<List<ExportRunResponse>>> Handle(ListExportRuns q, CancellationToken ct)
    {
        if (!await MayManage(q.PersonId, q.ShopId, ct)) return Result<List<ExportRunResponse>>.Fail("permission.denied", "Only the owner can see this.");
        return Result<List<ExportRunResponse>>.Ok((await store.Runs(q.ShopId, 60, ct)).OrderByDescending(r => r.Day).Select(AccountingMapping.ToResponse).ToList());
    }

    /// <summary>After fixing a mapping or reconnecting, the owner can send a failed day again at once; the retry count starts over.</summary>
    public async Task<Result<ExportRunResponse>> Handle(RetryExport c, CancellationToken ct)
    {
        if (!await MayManage(c.Request.PersonId, c.ShopId, ct)) return Result<ExportRunResponse>.Fail("permission.denied", "Only the owner can do this.");
        var run = await store.Run(c.RunId, ct);
        if (run is null || run.ShopId != c.ShopId) return Result<ExportRunResponse>.Fail("run.not_found", "Export not found.");
        if (run.Status == ExportStatus.Succeeded) return Result<ExportRunResponse>.Fail("run.already_sent", "This day was already sent.");
        run.Reopen();
        await Send(run, ct);
        await store.Save(ct);
        return Result<ExportRunResponse>.Ok(AccountingMapping.ToResponse(run));
    }

    /// <summary>
    /// Sends each finished day, from the day the connection starts (and at most a week back), to the package. A failure is kept and retried an hour later, up to five times,
    /// then the owner is told once. A day that was sent is never sent again.
    /// </summary>
    public async Task<Result<int>> Handle(RunAccountingExports c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = BusinessDay.Of(now);
        var sent = 0;
        foreach (var connection in await store.ActiveConnections(ct))
        {
            var first = connection.StartsOn > today.AddDays(-CatchUpDays) ? connection.StartsOn : today.AddDays(-CatchUpDays);
            var runs = (await store.RunsSince(connection.ShopId, connection.Provider, first, ct)).ToDictionary(r => r.Day);
            for (var day = first; day < today; day = day.AddDays(1))
            {
                if (!runs.TryGetValue(day, out var run))
                {
                    run = ExportRun.Start(connection.ShopId, connection.Provider, day, now);
                    store.Add(run);
                }
                if (!run.ShouldTry(now)) continue;
                if (await Send(run, ct)) sent++;
                await store.Save(ct);
            }
        }
        return Result<int>.Ok(sent);
    }

    private async Task<bool> Send(ExportRun run, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var connection = await store.Connection(run.ShopId, ct);
        var exporter = Exporter(run.Provider);
        if (connection is not { IsConnected: true } || exporter is null) { run.Failed("The accounting package is not connected.", now); return false; }

        var accounts = (await store.Mappings(run.ShopId, ct)).ToDictionary(m => m.Key, m => m.AccountCode);
        var f = await source.Day(run.ShopId, run.Day, ct);
        var journal = JournalBuilder.Build(new(f.Day, f.CashCents, f.CardCents, f.CreditCents, f.OtherCents, f.VatCents, f.CostOfSalesCents, f.PurchasesCents), accounts);
        if (!journal.IsSuccess) { run.Failed(journal.Error!.Message, now); await TellOwnerIfGivenUp(run, ct); return false; }
        if (journal.Value!.Lines.Count == 0) { run.Succeeded("nothing-to-post", now); return false; }   // a day with no trading has nothing to send

        var posted = await exporter.Post(connection.TokenReference, journal.Value, $"tillknowit:{run.ShopId:N}:{run.Day:yyyyMMdd}", ct);
        if (!posted.IsSuccess) { run.Failed(posted.Error!.Message, now); await TellOwnerIfGivenUp(run, ct); return false; }
        run.Succeeded(posted.Value!, now);
        return true;
    }

    private async Task TellOwnerIfGivenUp(ExportRun run, CancellationToken ct)
    {
        if (!run.GivenUp || run.OwnerTold) return;
        var shop = await shops.Get(run.ShopId, ct);
        if (shop?.OwnerPhone is not { Length: > 0 } phone) return;
        await queue.Queue(new QueueMessageRequest("accounting.failed", shop.OwnerLanguage, "whatsapp", phone,
            new() { ["shop"] = shop.Name, ["day"] = run.Day.ToString("d MMMM yyyy"), ["error"] = run.LastError ?? "" }, run.ShopId, $"accounting:{run.Id:N}:failed"), ct);
        run.NoteOwnerTold();
    }
}
