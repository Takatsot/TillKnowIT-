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
