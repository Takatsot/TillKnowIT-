using TillKnowIT.SharedKernel;

namespace Identity.Contracts;

/// <summary>The business account behind a supplier, and the people who work for it.</summary>
public interface ISupplierOrganisations
{
    /// <summary>Creates a pending supplier organisation and makes the person its admin. Returns the organisation id.</summary>
    Task<Result<Guid>> Create(string name, Guid adminPersonId, CancellationToken ct);

    /// <summary>Invites a sales rep. They get the supplier-rep role. Returns the membership id.</summary>
    Task<Result<Guid>> AddRep(Guid organisationId, string name, string phone, CancellationToken ct);

    /// <summary>Status is verified, suspended or pending. Verified suppliers can trade; the others cannot.</summary>
    Task<Result> SetStatus(Guid organisationId, string status, CancellationToken ct);
}

/// <summary>Whether a person may do something in one particular organisation, because of the role they hold there.</summary>
public interface IOrganisationAccess
{
    Task<bool> Has(Guid personId, Guid organisationId, string permission, CancellationToken ct);
}
