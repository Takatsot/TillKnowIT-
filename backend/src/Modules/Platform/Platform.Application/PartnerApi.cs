using Identity.Contracts;
using Platform.Contracts;
using Platform.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Platform.Application;

public interface IPartnerStore
{
    void Add(ApiClient client);
    Task<ApiClient?> Client(Guid id, CancellationToken ct);
    Task<ApiClient?> ClientByKeyHash(string hash, CancellationToken ct);
    Task<List<ApiClient>> Clients(CancellationToken ct);
    void Add(ApiGrant grant);
    Task<ApiGrant?> Grant(Guid clientId, Guid shopId, CancellationToken ct);
    Task<ApiGrant?> GrantById(Guid shopId, Guid grantId, CancellationToken ct);
    Task<List<ApiGrant>> GrantsOf(Guid shopId, CancellationToken ct);
    void Add(ApiCallLog log);
    Task Save(CancellationToken ct);
}

/// <summary>Counts calls per partner per minute. One instance counts in memory; running several API servers needs a shared counter behind this.</summary>
public interface IRateLimiter
{
    /// <summary>True if the call is within the limit and has been counted.</summary>
    bool TryTake(Guid clientId, int limitPerMinute, DateTimeOffset now);
}

public sealed record RegisterApiClient(RegisterClientRequest Request) : ICommand<ClientKeyResponse>;
public sealed record RotateApiKey(Guid ClientId, Guid CallerPersonId) : ICommand<ClientKeyResponse>;
public sealed record RevokeApiClient(Guid ClientId, Guid CallerPersonId) : ICommand<ClientResponse>;
public sealed record ListApiClients(Guid CallerPersonId) : IQuery<List<ClientResponse>>;
public sealed record GrantPartnerAccess(Guid ShopId, GrantAccessRequest Request) : ICommand<GrantResponse>;
public sealed record RevokePartnerAccess(Guid ShopId, Guid GrantId, Guid PersonId) : ICommand<GrantResponse>;
public sealed record ListPartnerGrants(Guid ShopId, Guid PersonId) : IQuery<List<GrantResponse>>;

/// <summary>Decides one partner call: the key must be good, the partner not revoked, the shop must have approved this scope, and the partner must be within its rate limit. Every call is logged.</summary>
public sealed record AuthorisePartnerCall(string? ApiKey, Guid ShopId, string Scope) : ICommand<PartnerCallResult>;

internal sealed class PartnerHandlers(IPartnerStore store, IPermissionChecker permissions, IOrganisationAccess access, IRateLimiter limiter, TimeProvider clock)
    : ICommandHandler<RegisterApiClient, ClientKeyResponse>, ICommandHandler<RotateApiKey, ClientKeyResponse>, ICommandHandler<RevokeApiClient, ClientResponse>,
      IQueryHandler<ListApiClients, List<ClientResponse>>, ICommandHandler<GrantPartnerAccess, GrantResponse>, ICommandHandler<RevokePartnerAccess, GrantResponse>,
      IQueryHandler<ListPartnerGrants, List<GrantResponse>>, ICommandHandler<AuthorisePartnerCall, PartnerCallResult>
{
    private static ClientResponse Respond(ApiClient c) => new(c.Id, c.Name, c.KeyPrefix, c.ScopeList.ToList(), c.RateLimitPerMinute, c.IsRevoked);

    private async Task<bool> Admin(Guid person, CancellationToken ct) => await permissions.Has(person, "partner.manage", ct);

    public async Task<Result<ClientKeyResponse>> Handle(RegisterApiClient c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await Admin(r.CallerPersonId, ct)) return Result<ClientKeyResponse>.Fail("permission.denied", "Only a platform admin can register partners.");
        var made = ApiClient.Register(r.Name, r.ContactEmail, r.Scopes, r.RateLimitPerMinute, clock.GetUtcNow());
        if (!made.IsSuccess) return Result<ClientKeyResponse>.Fail(made.Error!.Code, made.Error.Message);
        store.Add(made.Value.Client);
        await store.Save(ct);
        var client = made.Value.Client;
        return Result<ClientKeyResponse>.Ok(new(client.Id, client.Name, client.KeyPrefix, made.Value.Key, client.ScopeList.ToList(), client.RateLimitPerMinute));
    }

    public async Task<Result<ClientKeyResponse>> Handle(RotateApiKey c, CancellationToken ct)
    {
        if (!await Admin(c.CallerPersonId, ct)) return Result<ClientKeyResponse>.Fail("permission.denied", "Only a platform admin can rotate keys.");
        var client = await store.Client(c.ClientId, ct);
        if (client is null) return Result<ClientKeyResponse>.Fail("client.not_found", "Partner not found.");
        if (client.IsRevoked) return Result<ClientKeyResponse>.Fail("client.revoked", "This partner is no longer allowed.");
        var key = client.Rotate(clock.GetUtcNow());
        await store.Save(ct);
        return Result<ClientKeyResponse>.Ok(new(client.Id, client.Name, client.KeyPrefix, key, client.ScopeList.ToList(), client.RateLimitPerMinute));
    }

    public async Task<Result<ClientResponse>> Handle(RevokeApiClient c, CancellationToken ct)
    {
        if (!await Admin(c.CallerPersonId, ct)) return Result<ClientResponse>.Fail("permission.denied", "Only a platform admin can do this.");
        var client = await store.Client(c.ClientId, ct);
        if (client is null) return Result<ClientResponse>.Fail("client.not_found", "Partner not found.");
        client.Revoke();
        await store.Save(ct);
        return Result<ClientResponse>.Ok(Respond(client));
    }

    public async Task<Result<List<ClientResponse>>> Handle(ListApiClients q, CancellationToken ct) =>
        await Admin(q.CallerPersonId, ct) ? Result<List<ClientResponse>>.Ok((await store.Clients(ct)).OrderBy(x => x.Name).Select(Respond).ToList()) : Result<List<ClientResponse>>.Fail("permission.denied", "Only a platform admin can see partners.");

    /// <summary>The owner approves a partner for chosen scopes. Approving again changes the scopes and brings back an access that was taken away.</summary>
    public async Task<Result<GrantResponse>> Handle(GrantPartnerAccess c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await access.Has(r.PersonId, c.ShopId, "shop.settings", ct)) return Result<GrantResponse>.Fail("permission.denied", "Only the owner can approve a partner.");
        var client = await store.Client(r.ClientId, ct);
        if (client is null) return Result<GrantResponse>.Fail("client.not_found", "Partner not found.");
        var now = clock.GetUtcNow();
        var existing = await store.Grant(client.Id, c.ShopId, ct);
        if (existing is null)
        {
            var created = ApiGrant.Approve(client, c.ShopId, r.Scopes, r.PersonId, now);
            if (!created.IsSuccess) return Result<GrantResponse>.Fail(created.Error!.Code, created.Error.Message);
            store.Add(created.Value!);
            existing = created.Value!;
        }
        else
        {
            var checkedScopes = ApiGrant.Approve(client, c.ShopId, r.Scopes, r.PersonId, now);   // the same checks, then the existing row takes the new scopes
            if (!checkedScopes.IsSuccess) return Result<GrantResponse>.Fail(checkedScopes.Error!.Code, checkedScopes.Error.Message);
            existing.ChangeScopes(checkedScopes.Value!.ScopeList, r.PersonId, now);
        }
        await store.Save(ct);
        return Result<GrantResponse>.Ok(new(existing.Id, client.Id, client.Name, existing.ScopeList.ToList(), existing.ApprovedAt, existing.IsActive));
    }

    public async Task<Result<GrantResponse>> Handle(RevokePartnerAccess c, CancellationToken ct)
    {
        if (!await access.Has(c.PersonId, c.ShopId, "shop.settings", ct)) return Result<GrantResponse>.Fail("permission.denied", "Only the owner can take access away.");
        var grant = await store.GrantById(c.ShopId, c.GrantId, ct);
        if (grant is null) return Result<GrantResponse>.Fail("grant.not_found", "Access not found.");
        var done = grant.Revoke(clock.GetUtcNow());
        if (!done.IsSuccess) return Result<GrantResponse>.Fail(done.Error!.Code, done.Error.Message);
        await store.Save(ct);
        var client = await store.Client(grant.ClientId, ct);
        return Result<GrantResponse>.Ok(new(grant.Id, grant.ClientId, client?.Name ?? "", grant.ScopeList.ToList(), grant.ApprovedAt, grant.IsActive));
    }

    public async Task<Result<List<GrantResponse>>> Handle(ListPartnerGrants q, CancellationToken ct)
    {
        if (!await access.Has(q.PersonId, q.ShopId, "shop.settings", ct)) return Result<List<GrantResponse>>.Fail("permission.denied", "Only the owner can see this.");
        var grants = await store.GrantsOf(q.ShopId, ct);
        var clients = (await store.Clients(ct)).ToDictionary(x => x.Id);
        return Result<List<GrantResponse>>.Ok(grants.OrderByDescending(g => g.ApprovedAt).Select(g => new GrantResponse(g.Id, g.ClientId, clients.GetValueOrDefault(g.ClientId)?.Name ?? "", g.ScopeList.ToList(), g.ApprovedAt, g.IsActive)).ToList());
    }

    public async Task<Result<PartnerCallResult>> Handle(AuthorisePartnerCall c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        async Task<Result<PartnerCallResult>> Deny(Guid? clientId, string code, string message)
        {
            store.Add(new ApiCallLog(clientId, c.ShopId, c.Scope, code, now));
            await store.Save(ct);
            return Result<PartnerCallResult>.Fail(code, message);
        }

        if (string.IsNullOrWhiteSpace(c.ApiKey)) return await Deny(null, "api.key_missing", "Send your API key in the X-Api-Key header.");
        var client = await store.ClientByKeyHash(ApiKeys.Hash(c.ApiKey.Trim()), ct);
        if (client is null || !client.Matches(ApiKeys.Hash(c.ApiKey.Trim()), now)) return await Deny(client?.Id, "api.key_invalid", "That key is not valid.");
        if (!client.ScopeList.Contains(c.Scope)) return await Deny(client.Id, "api.scope_denied", "Your key is not allowed to read this.");
        var grant = await store.Grant(client.Id, c.ShopId, ct);
        if (grant is null || !grant.Allows(c.Scope)) return await Deny(client.Id, "api.not_approved", "The shop has not approved you to read this.");
        if (!limiter.TryTake(client.Id, client.RateLimitPerMinute, now)) return await Deny(client.Id, "api.rate_limited", "Too many calls. Slow down and try again in a minute.");

        store.Add(new ApiCallLog(client.Id, c.ShopId, c.Scope, "ok", now));
        await store.Save(ct);
        return Result<PartnerCallResult>.Ok(new(client.Id));
    }
}
