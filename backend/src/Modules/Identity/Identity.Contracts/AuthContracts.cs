namespace Identity.Contracts;

public sealed record RequestOtpRequest(string Phone);

/// <summary>Always the same answer whether or not the number belongs to someone, so nobody can find out who has an account. DevCode is only filled in development.</summary>
public sealed record RequestOtpResponse(string Message, string? DevCode);

public sealed record VerifyOtpRequest(string Phone, string Code);

public sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid PersonId, string DisplayName, string Language);
