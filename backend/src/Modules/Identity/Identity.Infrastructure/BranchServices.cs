using Identity.Application.Shops;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

internal sealed class BranchStore(IdentityDbContext db) : IBranchStore
{
    public Task<Organisation?> GetShop(Guid shopId, CancellationToken ct) => db.Organisations.FirstOrDefaultAsync(o => o.Id == shopId, ct);
    public Task<Membership?> MembershipAt(Guid personId, Guid organisationId, CancellationToken ct) => db.Memberships.FirstOrDefaultAsync(m => m.PersonId == personId && m.OrganisationId == organisationId, ct);
    public Task<Guid> RoleId(string code, CancellationToken ct) => db.Roles.Where(r => r.Code == code).Select(r => r.Id).FirstAsync(ct);

    public async Task Add(Organisation shop, Membership owner, CancellationToken ct)
    {
        await db.Organisations.AddAsync(shop, ct);
        await db.Memberships.AddAsync(owner, ct);
    }

    public Task<List<Organisation>> BranchesOf(Guid headId, CancellationToken ct) => db.Organisations.AsNoTracking().Where(o => o.ParentId == headId).ToListAsync(ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class BranchDirectory(IdentityDbContext db) : IBranchDirectory
{
    public async Task<Guid> HeadOf(Guid shopId, CancellationToken ct) =>
        await db.Organisations.AsNoTracking().Where(o => o.Id == shopId).Select(o => o.ParentId).FirstOrDefaultAsync(ct) ?? shopId;

    public async Task<List<BranchResponse>> Group(Guid shopId, CancellationToken ct)
    {
        var head = await HeadOf(shopId, ct);
        var rows = await db.Organisations.AsNoTracking().Where(o => o.Id == head || o.ParentId == head).ToListAsync(ct);
        return rows.OrderByDescending(o => o.Id == head).ThenBy(o => o.Name)
            .Select(o => new BranchResponse(o.Id, o.Name, o.Id == head, o.Status.ToString().ToLowerInvariant(), o.MunicipalityCode)).ToList();
    }
}
