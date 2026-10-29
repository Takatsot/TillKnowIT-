using Identity.Contracts;
using Marketplace.Contracts;
using Marketplace.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Suppliers;

public interface ISupplierLookup
{
    Task<Supplier?> ByOrganisation(Guid organisationId, CancellationToken ct);
}

/// <summary>The supplier behind an organisation, for the people who work there: they know the organisation, not the marketplace's supplier id.</summary>
public sealed record GetSupplierOfOrganisation(Guid OrganisationId, Guid CallerPersonId) : IQuery<SupplierIdentity>;

public sealed record SupplierIdentity(Guid SupplierId, Guid OrganisationId, string Name, string Status);

internal sealed class SupplierOfOrganisationHandler(ISupplierLookup lookup, IOrganisationAccess access) : IQueryHandler<GetSupplierOfOrganisation, SupplierIdentity>
{
    public async Task<Result<SupplierIdentity>> Handle(GetSupplierOfOrganisation q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.OrganisationId, "supplier.catalogue", ct) && !await access.Has(q.CallerPersonId, q.OrganisationId, "supplier.manage", ct))
            return Result<SupplierIdentity>.Fail("permission.denied", "You do not work for this supplier.");
        var supplier = await lookup.ByOrganisation(q.OrganisationId, ct);
        return supplier is null
            ? Result<SupplierIdentity>.Fail("supplier.not_found", "This business has no supplier application yet.")
            : Result<SupplierIdentity>.Ok(new(supplier.Id, q.OrganisationId, supplier.Name, supplier.Status.ToString().ToLowerInvariant()));
    }
}
