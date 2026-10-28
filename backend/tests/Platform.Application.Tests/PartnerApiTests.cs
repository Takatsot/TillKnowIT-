using Identity.Contracts;
using Platform.Application;
using Platform.Contracts;
using Platform.Domain;
using Platform.Infrastructure;

namespace Platform.Application.Tests;

public class PartnerApiTests
{
    private sealed class Store : IPartnerStore
    {
        public List<ApiClient> Clients_ { get; } = [];
        public List<ApiGrant> Grants { get; } = [];
        public List<ApiCallLog> Logs { get; } = [];
        public void Add(ApiClient c) => Clients_.Add(c);
        public Task<ApiClient?> Client(Guid id, CancellationToken ct) => Task.FromResult(Clients_.FirstOrDefault(c => c.Id == id));
        public Task<ApiClient?> ClientByKeyHash(string hash, CancellationToken ct) => Task.FromResult(Clients_.FirstOrDefault(c => c.KeyHash == hash || c.PreviousKeyHash == hash));
        public Task<List<ApiClient>> Clients(CancellationToken ct) => Task.FromResult(Clients_.ToList());
        public void Add(ApiGrant g) => Grants.Add(g);
        public Task<ApiGrant?> Grant(Guid clientId, Guid shopId, CancellationToken ct) => Task.FromResult(Grants.FirstOrDefault(g => g.ClientId == clientId && g.ShopId == shopId));
        public Task<ApiGrant?> GrantById(Guid shopId, Guid grantId, CancellationToken ct) => Task.FromResult(Grants.FirstOrDefault(g => g.ShopId == shopId && g.Id == grantId));
        public Task<List<ApiGrant>> GrantsOf(Guid shopId, CancellationToken ct) => Task.FromResult(Grants.Where(g => g.ShopId == shopId).ToList());
        public void Add(ApiCallLog l) => Logs.Add(l);
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class People : IPermissionChecker, IOrganisationAccess
    {
        public HashSet<Guid> Admins { get; } = [];
        public HashSet<Guid> Owners { get; } = [];
        public Task<bool> Has(Guid p, string perm, CancellationToken ct) => Task.FromResult(perm == "partner.manage" && Admins.Contains(p));
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(perm == "shop.settings" && Owners.Contains(p));
    }

    private readonly Store _store = new();
    private readonly People _people = new();
    private readonly InMemoryRateLimiter _limiter = new();
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _shop = Guid.NewGuid();

    public PartnerApiTests()
    {
        _people.Admins.Add(_admin);
        _people.Owners.Add(_owner);
    }

    private PartnerHandlers Handlers => new(_store, _people, _people, _limiter, _clock);

    private async Task<ClientKeyResponse> Register(int rate = 60, params string[] scopes) =>
        (await Handlers.Handle(new RegisterApiClient(new(_admin, "Bank of Spaza", "api@bank.example", scopes.Length == 0 ? [ApiScopes.SalesDaily, ApiScopes.SalesMonthly] : [.. scopes], rate)), default)).Value!;

    private async Task Approve(ClientKeyResponse client, params string[] scopes) =>
        Assert.True((await Handlers.Handle(new GrantPartnerAccess(_shop, new(_owner, client.ClientId, [.. scopes])), default)).IsSuccess);

    private Task<TillKnowIT.SharedKernel.Result<PartnerCallResult>> Call(string? key, string scope = ApiScopes.SalesDaily) =>
        Handlers.Handle(new AuthorisePartnerCall(key, _shop, scope), default);

    // ---- keys ----

    [Fact]
    public async Task A_new_partner_gets_a_key_once_and_only_its_hash_is_stored()
    {
        var client = await Register();

        Assert.StartsWith("tk_live_", client.Key);
        Assert.Equal(client.Key[..12], client.KeyPrefix);
        var stored = _store.Clients_.Single();
        Assert.NotEqual(client.Key, stored.KeyHash);
        Assert.Equal(64, stored.KeyHash.Length);
        Assert.DoesNotContain(client.Key, stored.KeyHash);
        Assert.Equal(ApiKeys.Hash(client.Key), stored.KeyHash);
        Assert.NotEqual(client.Key, (await Register()).Key);   // two partners never share a key
    }

    [Fact]
    public async Task Only_a_platform_admin_registers_lists_rotates_or_revokes_partners_and_scopes_and_limits_are_checked()
    {
        var req = new RegisterClientRequest(Guid.NewGuid(), "X", null, [ApiScopes.SalesDaily], 60);

        Assert.Equal("permission.denied", (await Handlers.Handle(new RegisterApiClient(req), default)).Error!.Code);
        Assert.Equal("client.scope_invalid", (await Handlers.Handle(new RegisterApiClient(req with { CallerPersonId = _admin, Scopes = ["sales.write"] }), default)).Error!.Code);
        Assert.Equal("client.scope_invalid", (await Handlers.Handle(new RegisterApiClient(req with { CallerPersonId = _admin, Scopes = [] }), default)).Error!.Code);
        Assert.Equal("client.rate_invalid", (await Handlers.Handle(new RegisterApiClient(req with { CallerPersonId = _admin, RateLimitPerMinute = 0 }), default)).Error!.Code);
        Assert.Equal("client.name_invalid", (await Handlers.Handle(new RegisterApiClient(req with { CallerPersonId = _admin, Name = " " }), default)).Error!.Code);
        var client = await Register();
        Assert.Equal("permission.denied", (await Handlers.Handle(new RotateApiKey(client.ClientId, Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new RevokeApiClient(client.ClientId, Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new ListApiClients(Guid.NewGuid()), default)).Error!.Code);
        Assert.DoesNotContain(client.Key, string.Join(",", (await Handlers.Handle(new ListApiClients(_admin), default)).Value!.Select(c => c.KeyPrefix + c.Name)));
    }

    [Fact]
    public async Task Rotating_a_key_keeps_the_old_one_working_for_a_day_then_only_the_new_one_works()
    {
        var client = await Register();
        await Approve(client, ApiScopes.SalesDaily);

        var rotated = (await Handlers.Handle(new RotateApiKey(client.ClientId, _admin), default)).Value!;

        Assert.NotEqual(client.Key, rotated.Key);
        Assert.True((await Call(rotated.Key)).IsSuccess);
        Assert.True((await Call(client.Key)).IsSuccess);   // no downtime while the partner switches over
        _clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal("api.key_invalid", (await Call(client.Key)).Error!.Code);
        Assert.True((await Call(rotated.Key)).IsSuccess);
    }

    [Fact]
    public async Task A_revoked_partner_stops_working_at_once_and_cannot_be_approved_again()
    {
        var client = await Register();
        await Approve(client, ApiScopes.SalesDaily);
        Assert.True((await Call(client.Key)).IsSuccess);

        await Handlers.Handle(new RevokeApiClient(client.ClientId, _admin), default);

        Assert.Equal("api.key_invalid", (await Call(client.Key)).Error!.Code);
        Assert.Equal("client.revoked", (await Handlers.Handle(new GrantPartnerAccess(_shop, new(_owner, client.ClientId, [ApiScopes.SalesDaily])), default)).Error!.Code);
        Assert.Equal("client.revoked", (await Handlers.Handle(new RotateApiKey(client.ClientId, _admin), default)).Error!.Code);
    }

    // ---- the owner approves each partner ----

    [Fact]
    public async Task A_partner_reads_only_what_the_shop_approved_and_nothing_before_it_does()
    {
        var client = await Register();

        Assert.Equal("api.not_approved", (await Call(client.Key)).Error!.Code);
        await Approve(client, ApiScopes.SalesDaily);
        Assert.True((await Call(client.Key, ApiScopes.SalesDaily)).IsSuccess);
        Assert.Equal("api.not_approved", (await Call(client.Key, ApiScopes.SalesMonthly)).Error!.Code);   // the partner may ask for it, the shop did not approve it
    }

    [Fact]
    public async Task The_partners_own_scopes_limit_what_a_shop_can_approve_and_a_scope_it_never_asked_for_is_refused()
    {
        var client = await Register(60, ApiScopes.SalesDaily);

        Assert.Equal("grant.scope_not_offered", (await Handlers.Handle(new GrantPartnerAccess(_shop, new(_owner, client.ClientId, [ApiScopes.SalesMonthly])), default)).Error!.Code);
        Assert.Equal("grant.scope_required", (await Handlers.Handle(new GrantPartnerAccess(_shop, new(_owner, client.ClientId, [])), default)).Error!.Code);
        Assert.Equal("api.scope_denied", (await Call(client.Key, ApiScopes.SalesMonthly)).Error!.Code);
    }

    [Fact]
    public async Task The_owner_takes_access_away_and_approving_again_brings_it_back_with_new_scopes()
    {
        var client = await Register();
        await Approve(client, ApiScopes.SalesDaily, ApiScopes.SalesMonthly);
        var grant = _store.Grants.Single();

        Assert.True((await Handlers.Handle(new RevokePartnerAccess(_shop, grant.Id, _owner), default)).IsSuccess);
        Assert.Equal("api.not_approved", (await Call(client.Key)).Error!.Code);
        Assert.Equal("grant.already_revoked", (await Handlers.Handle(new RevokePartnerAccess(_shop, grant.Id, _owner), default)).Error!.Code);

        await Approve(client, ApiScopes.SalesDaily);
        Assert.True((await Call(client.Key)).IsSuccess);
        Assert.Equal("api.not_approved", (await Call(client.Key, ApiScopes.SalesMonthly)).Error!.Code);
        Assert.Single(_store.Grants);
    }

    [Fact]
    public async Task Only_the_owner_approves_lists_or_withdraws_access()
    {
        var client = await Register();
        var stranger = Guid.NewGuid();

        Assert.Equal("permission.denied", (await Handlers.Handle(new GrantPartnerAccess(_shop, new(stranger, client.ClientId, [ApiScopes.SalesDaily])), default)).Error!.Code);
        await Approve(client, ApiScopes.SalesDaily);
        Assert.Equal("permission.denied", (await Handlers.Handle(new ListPartnerGrants(_shop, stranger), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new RevokePartnerAccess(_shop, _store.Grants.Single().Id, stranger), default)).Error!.Code);
        Assert.Equal("Bank of Spaza", (await Handlers.Handle(new ListPartnerGrants(_shop, _owner), default)).Value!.Single().ClientName);
        Assert.Equal("grant.not_found", (await Handlers.Handle(new RevokePartnerAccess(_shop, Guid.NewGuid(), _owner), default)).Error!.Code);
    }

    [Fact]
    public async Task A_shop_cannot_be_read_through_another_shops_approval()
    {
        var client = await Register();
        await Approve(client, ApiScopes.SalesDaily);

        var other = await Handlers.Handle(new AuthorisePartnerCall(client.Key, Guid.NewGuid(), ApiScopes.SalesDaily), default);

        Assert.Equal("api.not_approved", other.Error!.Code);
    }

    // ---- keys on the wire, rate limits, logs ----

    [Fact]
    public async Task A_missing_or_wrong_key_is_refused()
    {
        Assert.Equal("api.key_missing", (await Call(null)).Error!.Code);
        Assert.Equal("api.key_missing", (await Call("  ")).Error!.Code);
        Assert.Equal("api.key_invalid", (await Call("tk_live_nope")).Error!.Code);
    }

    [Fact]
    public async Task Calls_over_the_rate_limit_are_refused_until_the_next_minute()
    {
        var client = await Register(rate: 3);
        await Approve(client, ApiScopes.SalesDaily);

        for (var i = 0; i < 3; i++) Assert.True((await Call(client.Key)).IsSuccess);
        Assert.Equal("api.rate_limited", (await Call(client.Key)).Error!.Code);
        Assert.Equal("api.rate_limited", (await Call(client.Key)).Error!.Code);

        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await Call(client.Key)).IsSuccess);
    }

    [Fact]
    public async Task One_partners_rate_limit_does_not_slow_another()
    {
        var busy = await Register(rate: 1);
        var calm = await Register(rate: 1);
        await Approve(busy, ApiScopes.SalesDaily);
        await Approve(calm, ApiScopes.SalesDaily);

        await Call(busy.Key);
        Assert.Equal("api.rate_limited", (await Call(busy.Key)).Error!.Code);
        Assert.True((await Call(calm.Key)).IsSuccess);
    }

    [Fact]
    public async Task Every_call_is_logged_with_the_partner_the_scope_and_the_outcome_and_the_key_never_is()
    {
        var client = await Register(rate: 1);
        await Approve(client, ApiScopes.SalesDaily);

        await Call(client.Key);
        await Call(client.Key);
        await Call(client.Key, ApiScopes.SalesMonthly);
        await Call("tk_live_nope");

        Assert.Equal(["ok", "api.rate_limited", "api.not_approved", "api.key_invalid"], _store.Logs.Select(l => l.Outcome));
        Assert.Equal([client.ClientId, client.ClientId, client.ClientId, null], _store.Logs.Select(l => l.ClientId));
        Assert.Equal([ApiScopes.SalesDaily, ApiScopes.SalesDaily, ApiScopes.SalesMonthly, ApiScopes.SalesDaily], _store.Logs.Select(l => l.Scope));
        Assert.DoesNotContain(_store.Logs, l => l.Outcome.Contains(client.Key) || l.Scope.Contains(client.Key));
    }
}
