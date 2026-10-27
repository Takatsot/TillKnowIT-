namespace Identity.Contracts;

/// <summary>How to reach a person, for messages to them. Other modules ask through this and never read people tables.</summary>
public sealed record PersonContact(Guid PersonId, string DisplayName, string Phone, string? Email, string Language);

public interface IPersonDirectory
{
    Task<PersonContact?> Get(Guid personId, CancellationToken ct);
}
