using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Application;
using Platform.Contracts;
using Platform.Domain;
using Platform.Infrastructure.Persistence;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Platform.Infrastructure;

internal sealed class PartnerStore(PlatformDbContext db) : IPartnerStore
{
    public void Add(ApiClient client) => db.Set<ApiClient>().Add(client);
    public Task<ApiClient?> Client(Guid id, CancellationToken ct) => db.Set<ApiClient>().FirstOrDefaultAsync(c => c.Id == id, ct);
    public Task<ApiClient?> ClientByKeyHash(string hash, CancellationToken ct) => db.Set<ApiClient>().FirstOrDefaultAsync(c => c.KeyHash == hash || c.PreviousKeyHash == hash, ct);
    public Task<List<ApiClient>> Clients(CancellationToken ct) => db.Set<ApiClient>().AsNoTracking().ToListAsync(ct);
    public void Add(ApiGrant grant) => db.Set<ApiGrant>().Add(grant);
    public Task<ApiGrant?> Grant(Guid clientId, Guid shopId, CancellationToken ct) => db.Set<ApiGrant>().FirstOrDefaultAsync(g => g.ClientId == clientId && g.ShopId == shopId, ct);
    public Task<ApiGrant?> GrantById(Guid shopId, Guid grantId, CancellationToken ct) => db.Set<ApiGrant>().FirstOrDefaultAsync(g => g.ShopId == shopId && g.Id == grantId, ct);
    public Task<List<ApiGrant>> GrantsOf(Guid shopId, CancellationToken ct) => db.Set<ApiGrant>().AsNoTracking().Where(g => g.ShopId == shopId).ToListAsync(ct);
    public void Add(ApiCallLog log) => db.Set<ApiCallLog>().Add(log);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

/// <summary>A one-minute window per partner, counted in memory.</summary>
public sealed class InMemoryRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<Guid, (long Minute, int Count)> _windows = new();

    public bool TryTake(Guid clientId, int limitPerMinute, DateTimeOffset now)
    {
        var minute = now.ToUnixTimeSeconds() / 60;
        var allowed = false;
        _windows.AddOrUpdate(clientId, _ => { allowed = limitPerMinute >= 1; return (minute, 1); }, (_, w) =>
        {
            var current = w.Minute == minute ? w.Count : 0;
            allowed = current < limitPerMinute;
            return (minute, allowed ? current + 1 : current);
        });
        return allowed;
    }
}

internal sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> b)
    {
        b.ToTable("api_clients");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.ContactEmail).HasMaxLength(320);
        b.Property(x => x.KeyPrefix).HasMaxLength(20).IsRequired();
        b.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.PreviousKeyHash).HasMaxLength(64);
        b.Property(x => x.Scopes).HasMaxLength(200).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.ScopeList);
        b.HasIndex(x => x.KeyHash).IsUnique();
        b.HasIndex(x => x.PreviousKeyHash);
    }
}

internal sealed class ApiGrantConfiguration : IEntityTypeConfiguration<ApiGrant>
{
    public void Configure(EntityTypeBuilder<ApiGrant> b)
    {
        b.ToTable("api_grants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Scopes).HasMaxLength(200).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.IsActive);
        b.Ignore(x => x.ScopeList);
        b.HasIndex(x => new { x.ClientId, x.ShopId }).IsUnique();
    }
}

internal sealed class ApiCallLogConfiguration : IEntityTypeConfiguration<ApiCallLog>
{
    public void Configure(EntityTypeBuilder<ApiCallLog> b)
    {
        b.ToTable("api_call_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Scope).HasMaxLength(40).IsRequired();
        b.Property(x => x.Outcome).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.ClientId, x.CreatedAt });
    }
}

public static class PartnerEndpoints
{
    // TODO(auth): the admin and owner calls take the person id until sign-in exists. The partner calls are authorised by API key.
    public static IEndpointRouteBuilder MapPartnerEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/admin/api-clients", async (RegisterClientRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RegisterApiClient(req), ct)));
        api.MapGet("/admin/api-clients", async (Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListApiClients(callerPersonId), ct)));
        api.MapPost("/admin/api-clients/{id:guid}/rotate-key", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RotateApiKey(id, callerPersonId), ct)));
        api.MapPost("/admin/api-clients/{id:guid}/revoke", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RevokeApiClient(id, callerPersonId), ct)));

        api.MapPost("/shops/{shopId:guid}/partner-access", async (Guid shopId, GrantAccessRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new GrantPartnerAccess(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/partner-access", async (Guid shopId, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListPartnerGrants(shopId, personId), ct)));
        api.MapDelete("/shops/{shopId:guid}/partner-access/{grantId:guid}", async (Guid shopId, Guid grantId, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RevokePartnerAccess(shopId, grantId, personId), ct)));

        // The read-only partner API, version 1.
        api.MapGet("/partner/v1/shops/{shopId:guid}/sales/daily", async (Guid shopId, DateOnly day, HttpRequest request, IDispatcher d, IDailyAccountingSource source, CancellationToken ct) =>
        {
            var allowed = await d.Send(new AuthorisePartnerCall(request.Headers["X-Api-Key"].ToString(), shopId, ApiScopes.SalesDaily), ct);
            if (!allowed.IsSuccess) return Problem(allowed.Error!);
            var f = await source.Day(shopId, day, ct);
            return Results.Ok(new { day = f.Day, sales = f.Sales, grossCents = f.GrossCents, vatCents = f.VatCents, cashCents = f.CashCents, cardCents = f.CardCents, creditCents = f.CreditCents });
        });
        api.MapGet("/partner/v1/shops/{shopId:guid}/sales/monthly", async (Guid shopId, int? months, HttpRequest request, IDispatcher d, ISalesHistory history, CancellationToken ct) =>
        {
            var allowed = await d.Send(new AuthorisePartnerCall(request.Headers["X-Api-Key"].ToString(), shopId, ApiScopes.SalesMonthly), ct);
            if (!allowed.IsSuccess) return Problem(allowed.Error!);
            return Results.Ok(await history.Months(shopId, months ?? 12, ct));
        });
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Problem(Error error) => Results.Problem(title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: error.Code switch
        {
            "api.key_missing" or "api.key_invalid" => 401,
            "api.scope_denied" or "api.not_approved" or "permission.denied" => 403,
            "api.rate_limited" => 429,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) => 404,
            _ => 422,
        });
}
