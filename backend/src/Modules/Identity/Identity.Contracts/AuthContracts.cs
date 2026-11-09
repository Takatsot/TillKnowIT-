namespace Identity.Contracts;

/// <summary>SignUpName is filled in by someone who is new: if no one has this number yet, a person is made for it (the code sent to the number is what proves it is theirs).</summary>
public sealed record RequestOtpRequest(string Phone, string? SignUpName = null, string? Language = null);

/// <summary>Always the same answer whether or not the number belongs to someone, so nobody can find out who has an account. DevCode is only filled in development.</summary>
public sealed record RequestOtpResponse(string Message, string? DevCode);

public sealed record VerifyOtpRequest(string Phone, string Code);

public sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid PersonId, string DisplayName, string Language);
