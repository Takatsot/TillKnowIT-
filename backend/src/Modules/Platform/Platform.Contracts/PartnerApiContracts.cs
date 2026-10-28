namespace Platform.Contracts;

public sealed record RegisterClientRequest(Guid CallerPersonId, string Name, string? ContactEmail, List<string> Scopes, int RateLimitPerMinute);

/// <summary>Key is shown here once and never again: only its hash is kept.</summary>
public sealed record ClientKeyResponse(Guid ClientId, string Name, string KeyPrefix, string Key, List<string> Scopes, int RateLimitPerMinute);

public sealed record ClientResponse(Guid Id, string Name, string KeyPrefix, List<string> Scopes, int RateLimitPerMinute, bool Revoked);

public sealed record GrantAccessRequest(Guid PersonId, Guid ClientId, List<string> Scopes);

public sealed record GrantResponse(Guid Id, Guid ClientId, string ClientName, List<string> Scopes, DateTimeOffset ApprovedAt, bool Active);

/// <summary>What the partner-facing endpoints ask the platform to decide for each call: who the caller is and whether they may read this shop's data for this scope.</summary>
public sealed record PartnerCallResult(Guid ClientId);
