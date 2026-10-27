using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

internal sealed class OrganisationAccess(IdentityDbContext db) : IOrganisationAccess
{
    public Task<bool> Has(Guid personId, Guid organisationId, string permission, CancellationToken ct) =>
        db.Memberships.AsNoTracking().AnyAsync(m => m.PersonId == personId && m.OrganisationId == organisationId && m.Status == MembershipStatus.Active
            && db.Roles.Any(r => r.Id == m.RoleId && r.Permissions.Any(p => p.PermissionCode == permission)), ct);
}
