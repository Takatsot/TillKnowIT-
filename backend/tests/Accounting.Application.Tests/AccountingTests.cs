using Accounting.Application;
using Accounting.Contracts;
using Accounting.Domain;
using Accounting.Infrastructure;
using Billing.Contracts;
using Identity.Contracts;
using Messaging.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Contracts;
using TillKnowIT.SharedKernel;

namespace Accounting.Application.Tests;

public class JournalTests
{
    private static readonly IReadOnlyDictionary<string, string> Accounts = AccountKeys.All.ToDictionary(k => k, k => $"A-{k}");
    private static readonly DateOnly Day = new(2026, 10, 25);

    [Fact]
    public void Takings_vat_sales_cost_and_purchases_post_to_the_mapped_accounts_and_the_journal_balances()
    {
        var t = new DayTotals(Day, CashCents: 50_000, CardCents: 30_000, CreditCents: 10_000, OtherCents: 0, VatCents: 11_739, CostOfSalesCents: 60_000, PurchasesCents: 45_000);

        var journal = JournalBuilder.Build(t, Accounts).Value!;

        Assert.True(journal.Balances);
        Assert.Equal(90_000 + 60_000 + 45_000, journal.Debits);
        Assert.Equal(78_261, journal.Lines.Single(l => l.AccountKey == AccountKeys.Sales).CreditCents);
        Assert.Equal(11_739, journal.Lines.Single(l => l.AccountKey == AccountKeys.VatOutput).CreditCents);
        Assert.Equal("A-cash", journal.Lines.First().AccountCode);
        Assert.Equal(2, journal.Lines.Count(l => l.AccountKey == AccountKeys.Stock));   // sold out of stock, bought into stock
    }

    [Fact]
    public void Lines_with_nothing_in_them_are_left_out_and_a_day_with_no_trading_has_no_lines()
    {
        Assert.Empty(JournalBuilder.Build(new(Day, 0, 0, 0, 0, 0, 0, 0), Accounts).Value!.Lines);
        var cashOnly = JournalBuilder.Build(new(Day, 1_150, 0, 0, 0, 150, 0, 0), Accounts).Value!;
        Assert.Equal([AccountKeys.Cash, AccountKeys.Sales, AccountKeys.VatOutput], cashOnly.Lines.Select(l => l.AccountKey));
    }

    [Fact]
    public void A_missing_account_stops_the_whole_journal_and_names_what_to_choose()
    {
        var partial = Accounts.Where(a => a.Key != AccountKeys.VatOutput).ToDictionary(a => a.Key, a => a.Value);

        var result = JournalBuilder.Build(new(Day, 1_150, 0, 0, 0, 150, 0, 0), partial);

        Assert.Equal("accounting.mapping_incomplete", result.Error!.Code);
        Assert.Contains("vat_output", result.Error.Message);
    }

    [Fact]
    public void Figures_that_do_not_add_up_are_not_sent()
    {
        var result = JournalBuilder.Build(new(Day, 1_000, 0, 0, 0, 2_000, 0, 0), Accounts);   // more VAT than sales

        Assert.Equal("accounting.unbalanced", result.Error!.Code);
    }

    [Fact]
    public void An_export_run_retries_an_hour_later_up_to_five_times_then_is_given_up_and_the_owner_reopens_it()
    {
        var now = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
        var run = ExportRun.Start(Guid.NewGuid(), "xero", Day, now);
        Assert.True(run.ShouldTry(now));

        for (var i = 0; i < 5; i++)
        {
            run.Failed("boom", now.AddHours(i));
            Assert.False(run.ShouldTry(now.AddHours(i).AddMinutes(30)));
        }

        Assert.True(run.GivenUp);
        Assert.False(run.ShouldTry(now.AddDays(5)));
        run.Reopen();
        Assert.True(run.ShouldTry(now.AddDays(5)));
        run.Succeeded("ref", now.AddDays(5));
        run.Reopen();
        Assert.Equal(ExportStatus.Succeeded, run.Status);   // a sent day stays sent
    }
}

public class AccountingFlowTests
{
    private sealed class Store : IAccountingStore
    {
        public List<AccountingConnection> Connections { get; } = [];
        public List<AccountMapping> MappingRows { get; } = [];
        public List<ExportRun> RunRows { get; } = [];
        public void Add(AccountingConnection c) => Connections.Add(c);
        public Task<AccountingConnection?> Connection(Guid shopId, CancellationToken ct) => Task.FromResult(Connections.FirstOrDefault(c => c.ShopId == shopId));
        public Task<List<AccountingConnection>> ActiveConnections(CancellationToken ct) => Task.FromResult(Connections.Where(c => c.IsConnected).ToList());
        public void Add(AccountMapping m) => MappingRows.Add(m);
        public Task<List<AccountMapping>> Mappings(Guid shopId, CancellationToken ct) => Task.FromResult(MappingRows.Where(m => m.ShopId == shopId).ToList());
        public void Add(ExportRun r) => RunRows.Add(r);
        public Task<ExportRun?> Run(Guid id, CancellationToken ct) => Task.FromResult(RunRows.FirstOrDefault(r => r.Id == id));
        public Task<ExportRun?> RunFor(Guid shopId, string provider, DateOnly day, CancellationToken ct) => Task.FromResult(RunRows.FirstOrDefault(r => r.ShopId == shopId && r.Provider == provider && r.Day == day));
        public Task<List<ExportRun>> Runs(Guid shopId, int take, CancellationToken ct) => Task.FromResult(RunRows.Where(r => r.ShopId == shopId).ToList());
        public Task<List<ExportRun>> RunsSince(Guid shopId, string provider, DateOnly from, CancellationToken ct) => Task.FromResult(RunRows.Where(r => r.ShopId == shopId && r.Provider == provider && r.Day >= from).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Access : IOrganisationAccess
    {
        public HashSet<Guid> Owners { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Owners.Contains(p) && perm == "shop.settings");
    }

    private sealed class Plans : IEntitlements
    {
        public bool Export { get; set; } = true;
        public Task<bool> Has(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult(Export && feature == PlanFeatures.AccountingExport);
        public Task<long?> LimitFor(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult<long?>(null);
    }

    private sealed class Branches : IBranchDirectory
    {
        public Task<Guid> HeadOf(Guid shopId, CancellationToken ct) => Task.FromResult(shopId);
        public Task<List<BranchResponse>> Group(Guid shopId, CancellationToken ct) => Task.FromResult(new List<BranchResponse>());
    }

    private sealed class Source : IDailyAccountingSource
    {
        public DayFigures Figures { get; set; } = new(default, 4, 50_000, 30_000, 0, 0, 10_435, 40_000, 20_000);
        public int Asked { get; private set; }
        public Task<DayFigures> Day(Guid shopId, DateOnly day, CancellationToken ct) { Asked++; return Task.FromResult(Figures with { Day = day }); }
    }

    private sealed class Shops : IShopDirectory
    {
        public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) => Task.FromResult<ShopInfo?>(new(shopId, "Thandi Spaza", "verified", Guid.NewGuid(), "+27821234567", null, "en"));
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(new List<Guid>());
    }

    private sealed class Messages : IMessageQueue
    {
        public List<QueueMessageRequest> Sent { get; } = [];
        public Task<Result<QueueMessageResult>> Queue(QueueMessageRequest r, CancellationToken ct) { Sent.Add(r); return Task.FromResult(Result<QueueMessageResult>.Ok(new(Guid.NewGuid(), false))); }
    }

    private sealed class Package(string provider) : IAccountingExporter
    {
        public bool Fail { get; set; }
        public List<(string Key, Journal Journal)> Posted { get; } = [];
        public string Provider => provider;
        public Task<Result<ExchangeResult>> Exchange(string code, CancellationToken ct) => Task.FromResult(code == "bad" ? Result<ExchangeResult>.Fail("x", "no") : Result<ExchangeResult>.Ok(new("vault://token-1", "Thandi Trading")));
        public Task<Result<string>> Post(string token, Journal journal, string key, CancellationToken ct)
        {
            if (Fail) return Task.FromResult(Result<string>.Fail("package.down", "The package said no."));
            Posted.Add((key, journal));
            return Task.FromResult(Result<string>.Ok($"ref-{Posted.Count}"));
        }
    }

    private readonly Store _store = new();
    private readonly Access _access = new();
    private readonly Plans _plans = new();
    private readonly Source _source = new();
    private readonly Messages _messages = new();
    private readonly Package _xero = new("xero");
    private readonly Package _sage = new("sage");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));   // Monday 10:00 in South Africa
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();

    public AccountingFlowTests() => _access.Owners.Add(_owner);

    private AccountingHandlers Handlers => new(_store, [_xero, _sage], _access, _plans, new Branches(), _source, new Shops(), _messages, _clock);

    private async Task Connect(DateOnly? startsOn = null, string provider = "xero") =>
        Assert.True((await Handlers.Handle(new ConnectAccounting(_shop, provider, new(_owner, "code", startsOn ?? new DateOnly(2026, 10, 23))), default)).IsSuccess);

    private Task MapEverything() => Handlers.Handle(new SaveAccountMappings(_shop, new(_owner, AccountKeys.All.Select(k => new MappingEntry(k, $"A-{k}")).ToList())), default);

    // ---- connecting and mapping ----

    [Fact]
    public async Task The_owner_connects_a_package_and_only_a_reference_to_the_access_is_kept()
    {
        var connected = (await Handlers.Handle(new ConnectAccounting(_shop, "xero", new(_owner, "code", null)), default)).Value!;

        Assert.Equal(("xero", "Thandi Trading", true, new DateOnly(2026, 10, 26)), (connected.Provider, connected.TenantName, connected.Connected, connected.StartsOn));
        Assert.Equal("vault://token-1", _store.Connections.Single().TokenReference);
    }

    [Fact]
    public async Task Connecting_needs_the_owner_the_plan_a_known_package_and_a_working_sign_in()
    {
        Assert.Equal("permission.denied", (await Handlers.Handle(new ConnectAccounting(_shop, "xero", new(Guid.NewGuid(), "code", null)), default)).Error!.Code);
        Assert.Equal("accounting.provider_unknown", (await Handlers.Handle(new ConnectAccounting(_shop, "quickbooks", new(_owner, "code", null)), default)).Error!.Code);
        Assert.Equal("accounting.connect_failed", (await Handlers.Handle(new ConnectAccounting(_shop, "xero", new(_owner, "bad", null)), default)).Error!.Code);
        _plans.Export = false;
        Assert.Equal("plan.feature_not_included", (await Handlers.Handle(new ConnectAccounting(_shop, "xero", new(_owner, "code", null)), default)).Error!.Code);
    }

    [Fact]
    public async Task A_second_package_needs_the_first_disconnected_and_reconnecting_works_again()
    {
        await Connect();
        Assert.Equal("accounting.already_connected", (await Handlers.Handle(new ConnectAccounting(_shop, "sage", new(_owner, "code", null)), default)).Error!.Code);

        Assert.False((await Handlers.Handle(new DisconnectAccounting(_shop, _owner), default)).Value!.Connected);
        Assert.Equal("accounting.not_connected", (await Handlers.Handle(new DisconnectAccounting(_shop, _owner), default)).Error!.Code);
        await Connect(provider: "sage");
        Assert.Equal("sage", _store.Connections.Single().Provider);
    }

    [Fact]
    public async Task Mappings_are_saved_per_account_changed_in_place_and_what_is_missing_is_listed()
    {
        var first = (await Handlers.Handle(new SaveAccountMappings(_shop, new(_owner, [new("cash", "1000"), new("sales", "4000")])), default)).Value!;
        Assert.Equal(7, first.Missing.Count);

        await Handlers.Handle(new SaveAccountMappings(_shop, new(_owner, [new("cash", "1010")])), default);

        Assert.Equal("1010", _store.MappingRows.Single(m => m.Key == "cash").AccountCode);
        Assert.Equal(2, _store.MappingRows.Count);
        Assert.Equal("mapping.key_unknown", (await Handlers.Handle(new SaveAccountMappings(_shop, new(_owner, [new("lunch", "1")])), default)).Error!.Code);
        Assert.Equal("mapping.account_invalid", (await Handlers.Handle(new SaveAccountMappings(_shop, new(_owner, [new("cash", " ")])), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new SaveAccountMappings(_shop, new(Guid.NewGuid(), [new("cash", "1")])), default)).Error!.Code);
    }

    // ---- the daily run ----

    [Fact]
    public async Task Each_finished_day_since_the_start_is_sent_once_and_running_again_sends_nothing_twice()
    {
        await Connect(new DateOnly(2026, 10, 23));
        await MapEverything();

        Assert.Equal(3, (await Handlers.Handle(new RunAccountingExports(), default)).Value);   // 23, 24 and 25 October; today is not finished
        Assert.Equal(0, (await Handlers.Handle(new RunAccountingExports(), default)).Value);

        Assert.Equal(["tillknowit:" + _shop.ToString("N") + ":20261023", "tillknowit:" + _shop.ToString("N") + ":20261024", "tillknowit:" + _shop.ToString("N") + ":20261025"], _xero.Posted.Select(p => p.Key));
        Assert.All(_xero.Posted, p => Assert.True(p.Journal.Balances));
        Assert.All(_store.RunRows, r => Assert.Equal(ExportStatus.Succeeded, r.Status));
    }

    [Fact]
    public async Task Days_before_the_connection_starts_and_more_than_a_week_back_are_never_sent()
    {
        await Connect(new DateOnly(2026, 9, 1));
        await MapEverything();

        await Handlers.Handle(new RunAccountingExports(), default);

        Assert.Equal(7, _xero.Posted.Count);
        Assert.Equal(new DateOnly(2026, 10, 19), _store.RunRows.Min(r => r.Day));
    }

    [Fact]
    public async Task A_day_with_no_trading_has_nothing_to_send()
    {
        _source.Figures = new(default, 0, 0, 0, 0, 0, 0, 0, 0);
        await Connect(new DateOnly(2026, 10, 25));
        await MapEverything();

        Assert.Equal(0, (await Handlers.Handle(new RunAccountingExports(), default)).Value);

        Assert.Empty(_xero.Posted);
        Assert.Equal(ExportStatus.Succeeded, _store.RunRows.Single().Status);
    }

    [Fact]
    public async Task A_failure_is_kept_retried_an_hour_later_and_the_owner_is_told_once_after_five_tries()
    {
        await Connect(new DateOnly(2026, 10, 25));
        await MapEverything();
        _xero.Fail = true;

        for (var hour = 0; hour < 8; hour++)
        {
            await Handlers.Handle(new RunAccountingExports(), default);
            _clock.Advance(TimeSpan.FromHours(1));
        }

        var run = _store.RunRows.Single();
        Assert.Equal((ExportStatus.Failed, 5, true, "The package said no."), (run.Status, run.Attempts, run.GivenUp, run.LastError));
        var message = Assert.Single(_messages.Sent);
        Assert.Equal(("accounting.failed", "+27821234567"), (message.TemplateCode, message.Destination));
        Assert.Contains("25 October 2026", message.Values["day"]);
    }

    [Fact]
    public async Task A_run_that_failed_for_a_missing_mapping_succeeds_when_the_owner_fixes_it_and_retries()
    {
        await Connect(new DateOnly(2026, 10, 25));
        await Handlers.Handle(new RunAccountingExports(), default);
        var run = _store.RunRows.Single();
        Assert.Contains("Choose an account", run.LastError);

        await MapEverything();
        var retried = (await Handlers.Handle(new RetryExport(_shop, run.Id, new(_owner)), default)).Value!;

        Assert.Equal(("succeeded", 1), (retried.Status, _xero.Posted.Count));
        Assert.Equal("run.already_sent", (await Handlers.Handle(new RetryExport(_shop, run.Id, new(_owner)), default)).Error!.Code);
        Assert.Equal("run.not_found", (await Handlers.Handle(new RetryExport(_shop, Guid.NewGuid(), new(_owner)), default)).Error!.Code);
    }

    [Fact]
    public async Task Disconnecting_stops_the_sending_and_the_run_list_is_for_the_owner()
    {
        await Connect(new DateOnly(2026, 10, 25));
        await MapEverything();
        await Handlers.Handle(new DisconnectAccounting(_shop, _owner), default);

        await Handlers.Handle(new RunAccountingExports(), default);

        Assert.Empty(_xero.Posted);
        Assert.Equal("permission.denied", (await Handlers.Handle(new ListExportRuns(_shop, Guid.NewGuid()), default)).Error!.Code);
        Assert.Empty((await Handlers.Handle(new ListExportRuns(_shop, _owner), default)).Value!);
    }

    [Fact]
    public async Task The_package_in_use_is_the_one_connected_so_a_sage_shop_never_posts_to_xero()
    {
        await Connect(new DateOnly(2026, 10, 25), "sage");
        await MapEverything();

        await Handlers.Handle(new RunAccountingExports(), default);

        Assert.Single(_sage.Posted);
        Assert.Empty(_xero.Posted);
    }

    [Fact]
    public async Task The_dev_package_remembers_a_key_and_returns_the_same_reference_for_a_repeat()
    {
        var dev = new DevAccountingExporter("xero");
        var journal = new Journal(new DateOnly(2026, 10, 25), []);

        var one = (await dev.Post("t", journal, "k", default)).Value;
        var two = (await dev.Post("t", journal, "k", default)).Value;

        Assert.Equal(one, two);
    }
}
