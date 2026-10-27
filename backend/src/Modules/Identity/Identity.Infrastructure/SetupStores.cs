using Identity.Application.Setup;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

internal sealed class SetupStore(IdentityDbContext db) : ISetupStore
{
    public Task<Organisation?> GetShop(Guid shopId, CancellationToken ct) => db.Organisations.FirstOrDefaultAsync(o => o.Id == shopId, ct);

    public Task<List<SetupStep>> Steps(Guid shopId, CancellationToken ct) => db.Set<SetupStep>().AsNoTracking().Where(s => s.ShopId == shopId).ToListAsync(ct);

    public async Task Add(SetupStep step, CancellationToken ct) => await db.Set<SetupStep>().AddAsync(step, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class MembershipReadStore(IdentityDbContext db) : IMembershipReadStore
{
    public async Task<List<MembershipResponse>> ForPerson(Guid personId, CancellationToken ct)
    {
        var rows = await (from m in db.Memberships.AsNoTracking()
                          join o in db.Organisations.AsNoTracking() on m.OrganisationId equals o.Id
                          join r in db.Roles.AsNoTracking() on m.RoleId equals r.Id
                          where m.PersonId == personId && m.Status == MembershipStatus.Active && o.Status != OrganisationStatus.Suspended
                          select new { m.Id, OrganisationId = o.Id, o.Name, o.Type, r.Code }).ToListAsync(ct);
        return rows.Select(r => new MembershipResponse(r.Id, r.OrganisationId, r.Name, r.Type.ToString().ToLowerInvariant(), r.Code)).ToList();
    }
}

internal sealed class PermissionChecker(IdentityDbContext db) : IPermissionChecker
{
    public Task<bool> Has(Guid personId, string permission, CancellationToken ct) =>
        db.Memberships.AsNoTracking().AnyAsync(m => m.PersonId == personId && m.Status == MembershipStatus.Active
            && db.Roles.Any(r => r.Id == m.RoleId && r.Permissions.Any(p => p.PermissionCode == permission)), ct);
}
