using Identity.Application.Staff;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

internal sealed class StaffStore(IdentityDbContext db) : IStaffStore
{
    public Task<Organisation?> GetShop(Guid shopId, CancellationToken ct) =>
        db.Organisations.FirstOrDefaultAsync(o => o.Id == shopId, ct);

    public Task<Role?> FindRole(string code, CancellationToken ct) =>
        db.Roles.FirstOrDefaultAsync(r => r.Code == code, ct);

    public Task<Person?> FindPersonByPhone(string phone, CancellationToken ct) =>
        db.People.FirstOrDefaultAsync(p => p.Phone == phone, ct);

    public Task<Membership?> GetMembership(Guid membershipId, CancellationToken ct) =>
        db.Memberships.FirstOrDefaultAsync(m => m.Id == membershipId, ct);

    public Task<bool> MembershipExists(Guid personId, Guid organisationId, CancellationToken ct) =>
        db.Memberships.AnyAsync(m => m.PersonId == personId && m.OrganisationId == organisationId, ct);

    public async Task Add(Person person, CancellationToken ct) => await db.People.AddAsync(person, ct);

    public async Task Add(Membership membership, CancellationToken ct) => await db.Memberships.AddAsync(membership, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
