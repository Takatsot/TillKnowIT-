using Identity.Contracts;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

internal sealed class PersonDirectory(IdentityDbContext db) : IPersonDirectory
{
    public async Task<PersonContact?> Get(Guid personId, CancellationToken ct)
    {
        var p = await db.People.AsNoTracking().Where(x => x.Id == personId).Select(x => new { x.Id, x.DisplayName, x.Phone, x.Email, x.PreferredLanguage }).FirstOrDefaultAsync(ct);
        return p is null ? null : new PersonContact(p.Id, p.DisplayName, p.Phone, p.Email, p.PreferredLanguage);
    }
}

internal sealed class StaffDirectory(Persistence.IdentityDbContext db) : IStaffDirectory
{
    public async Task<Dictionary<Guid, string>> Names(Guid shopId, IReadOnlyCollection<Guid> staffIds, CancellationToken ct) =>
        await (from m in db.Memberships.AsNoTracking()
               join p in db.People.AsNoTracking() on m.PersonId equals p.Id
               where m.OrganisationId == shopId && staffIds.Contains(m.Id)
               select new { m.Id, p.DisplayName }).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
}
