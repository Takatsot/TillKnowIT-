namespace Identity.Contracts;

/// <summary>How to reach a person, for messages to them. Other modules ask through this and never read people tables.</summary>
public sealed record PersonContact(Guid PersonId, string DisplayName, string Phone, string? Email, string Language);

public interface IPersonDirectory
{
    Task<PersonContact?> Get(Guid personId, CancellationToken ct);
}

/// <summary>Names for staff, for reports. A staff id is the membership id the till unlocks with.</summary>
public interface IStaffDirectory
{
    /// <summary>The display name of each staff member of the shop that could be found. Ids that are not staff of this shop are left out.</summary>
    Task<Dictionary<Guid, string>> Names(Guid shopId, IReadOnlyCollection<Guid> staffIds, CancellationToken ct);
}
