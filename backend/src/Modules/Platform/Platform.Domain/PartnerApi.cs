using System.Security.Cryptography;
using System.Text;
using TillKnowIT.SharedKernel;

namespace Platform.Domain;

/// <summary>What a partner can be allowed to read. Read-only: nothing here changes a shop's data.</summary>
public static class ApiScopes
{
    public const string SalesDaily = "sales.daily";
    public const string SalesMonthly = "sales.monthly";

    public static readonly string[] All = [SalesDaily, SalesMonthly];
}

public static class ApiKeys
{
    public static readonly TimeSpan RotationOverlap = TimeSpan.FromHours(24);

    /// <summary>A new random key. It is shown once; only its hash is kept. The prefix lets people tell keys apart without seeing them.</summary>
    public static (string Key, string Prefix, string Hash) Generate()
    {
        var key = "tk_live_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (key, key[..12], Hash(key));
    }

    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}

/// <summary>G6-3: a partner (a bank, a bookkeeper, an app) that reads data through the public API. It only sees a shop that approved it.</summary>
public sealed class ApiClient : VersionedEntity
{
    private ApiClient() { }

    public string Name { get; private set; } = default!;
    public string? ContactEmail { get; private set; }
    public string KeyPrefix { get; private set; } = default!;
    public string KeyHash { get; private set; } = default!;
    public string? PreviousKeyHash { get; private set; }
    public DateTimeOffset? PreviousValidUntil { get; private set; }
    public string Scopes { get; private set; } = "";
    public int RateLimitPerMinute { get; private set; }
    public bool IsRevoked { get; private set; }

    public IReadOnlyList<string> ScopeList => Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static Result<(ApiClient Client, string Key)> Register(string? name, string? contactEmail, IEnumerable<string>? scopes, int rateLimitPerMinute, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result<(ApiClient, string)>.Fail("client.name_invalid", "A partner needs a name.");
        var list = (scopes ?? []).Select(s => s.Trim().ToLowerInvariant()).Distinct().Order().ToList();
        if (list.Count == 0 || list.Any(s => !ApiScopes.All.Contains(s))) return Result<(ApiClient, string)>.Fail("client.scope_invalid", $"Choose from: {string.Join(", ", ApiScopes.All)}.");
        if (rateLimitPerMinute is < 1 or > 6000) return Result<(ApiClient, string)>.Fail("client.rate_invalid", "The rate limit is between 1 and 6000 calls a minute.");
        var (key, prefix, hash) = ApiKeys.Generate();
        var c = new ApiClient { Name = name.Trim(), ContactEmail = contactEmail?.Trim(), KeyPrefix = prefix, KeyHash = hash, Scopes = string.Join(',', list), RateLimitPerMinute = rateLimitPerMinute };
        c.StampFromDevice(Guid.CreateVersion7(), now, null, null);
        return Result<(ApiClient, string)>.Ok((c, key));
    }

    /// <summary>A new key takes over at once and the old one keeps working for a day, so the partner can switch without downtime.</summary>
    public string Rotate(DateTimeOffset now)
    {
        var (key, prefix, hash) = ApiKeys.Generate();
        PreviousKeyHash = KeyHash;
        PreviousValidUntil = now + ApiKeys.RotationOverlap;
        KeyHash = hash;
        KeyPrefix = prefix;
        Touch();
        return key;
    }

    public bool Matches(string keyHash, DateTimeOffset now) =>
        !IsRevoked && (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(keyHash), Encoding.ASCII.GetBytes(KeyHash))
                       || (PreviousKeyHash is not null && PreviousValidUntil > now && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(keyHash), Encoding.ASCII.GetBytes(PreviousKeyHash))));

    public void Revoke()
    {
        IsRevoked = true;
        Touch();
    }
}

/// <summary>A shop owner's approval of one partner for some scopes. The owner can take it back at any time.</summary>
public sealed class ApiGrant : VersionedEntity
{
    private ApiGrant() { }

    public Guid ClientId { get; private set; }
    public Guid ShopId { get; private set; }
    public string Scopes { get; private set; } = "";
    public Guid ApprovedBy { get; private set; }
    public DateTimeOffset ApprovedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;
    public IReadOnlyList<string> ScopeList => Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static Result<ApiGrant> Approve(ApiClient client, Guid shopId, IEnumerable<string>? scopes, Guid personId, DateTimeOffset now)
    {
        var list = (scopes ?? []).Select(s => s.Trim().ToLowerInvariant()).Distinct().Order().ToList();
        if (list.Count == 0) return Result<ApiGrant>.Fail("grant.scope_required", "Choose what the partner may read.");
        if (client.IsRevoked) return Result<ApiGrant>.Fail("client.revoked", "This partner is no longer allowed.");
        if (list.Any(s => !client.ScopeList.Contains(s))) return Result<ApiGrant>.Fail("grant.scope_not_offered", "That partner has not asked for all of those.");
        var g = new ApiGrant { ClientId = client.Id, ShopId = shopId, Scopes = string.Join(',', list), ApprovedBy = personId, ApprovedAt = now };
        g.StampFromDevice(Guid.CreateVersion7(), now, personId, null);
        return Result<ApiGrant>.Ok(g);
    }

    public void ChangeScopes(IEnumerable<string> scopes, Guid personId, DateTimeOffset now)
    {
        Scopes = string.Join(',', scopes.Select(s => s.Trim().ToLowerInvariant()).Distinct().Order());
        ApprovedBy = personId;
        ApprovedAt = now;
        RevokedAt = null;
        Touch();
    }

    public Result Revoke(DateTimeOffset now)
    {
        if (!IsActive) return Result.Fail("grant.already_revoked", "This access was already taken away.");
        RevokedAt = now;
        Touch();
        return Result.Ok();
    }

    public bool Allows(string scope) => IsActive && ScopeList.Contains(scope);
}

/// <summary>Every call a partner makes, allowed or not, with who and what scope. Append only.</summary>
public sealed class ApiCallLog : Entity
{
    private ApiCallLog() { }

    public Guid? ClientId { get; private set; }
    public Guid? ShopId { get; private set; }
    public string Scope { get; private set; } = default!;
    public string Outcome { get; private set; } = default!;

    public ApiCallLog(Guid? clientId, Guid? shopId, string scope, string outcome, DateTimeOffset at)
    {
        ClientId = clientId;
        ShopId = shopId;
        Scope = scope;
        Outcome = outcome;
        CreatedAt = at;
    }
}
