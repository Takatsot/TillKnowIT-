using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

internal sealed class ShopDirectory(IdentityDbContext db) : IShopDirectory
{
    public async Task<ShopInfo?> Get(Guid shopId, CancellationToken ct)
    {
        var shop = await db.Organisations.AsNoTracking()
            .Where(o => o.Id == shopId && o.Type == OrganisationType.Shop)
            .Select(o => new { o.Id, o.Name, o.Status })
            .FirstOrDefaultAsync(ct);
        if (shop is null) return null;

        // The owner is the member with the owner role.
        var owner = await (from m in db.Memberships.AsNoTracking()
                           join r in db.Roles.AsNoTracking() on m.RoleId equals r.Id
                           join p in db.People.AsNoTracking() on m.PersonId equals p.Id
                           where m.OrganisationId == shopId && r.Code == "owner" && m.Status == MembershipStatus.Active
                           orderby m.CreatedAt
                           select new { p.Id, p.Phone, p.Email, p.PreferredLanguage }).FirstOrDefaultAsync(ct);

        return new ShopInfo(shop.Id, shop.Name, shop.Status.ToString().ToLowerInvariant(), owner?.Id, owner?.Phone, owner?.Email, owner?.PreferredLanguage ?? "en");
    }

    public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) =>
        db.Organisations.AsNoTracking()
            .Where(o => o.Type == OrganisationType.Shop && o.Status == OrganisationStatus.Verified)
            .Select(o => o.Id).ToListAsync(ct);
}
