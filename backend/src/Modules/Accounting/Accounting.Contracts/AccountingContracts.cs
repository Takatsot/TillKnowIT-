namespace Accounting.Contracts;

/// <summary>AuthorisationCode comes back from the package's sign-in screen (OAuth). StartsOn is the first day to send; earlier days are not.</summary>
public sealed record ConnectRequest(Guid PersonId, string AuthorisationCode, DateOnly? StartsOn);

public sealed record ConnectionResponse(Guid Id, string Provider, string? TenantName, bool Connected, DateOnly StartsOn, DateTimeOffset ConnectedAt);

public sealed record MappingEntry(string Key, string AccountCode);

public sealed record SaveMappingsRequest(Guid PersonId, List<MappingEntry> Mappings);

public sealed record MappingsResponse(List<MappingEntry> Mappings, List<string> Missing);

public sealed record ExportRunResponse(Guid Id, string Provider, DateOnly Day, string Status, int Attempts, string? Error, string? ExternalReference, bool GivenUp);

public sealed record RetryRequest(Guid PersonId);
