using Identity.Application.Shops;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

internal sealed class ShopListStore(IdentityDbContext db) : IShopListStore
{
    public async Task<List<StaffListItem>> Staff(Guid shopId, CancellationToken ct) =>
        await (from m in db.Memberships.AsNoTracking()
               join p in db.People.AsNoTracking() on m.PersonId equals p.Id
               join r in db.Roles.AsNoTracking() on m.RoleId equals r.Id
               where m.OrganisationId == shopId
               select new StaffListItem(m.Id, p.DisplayName, p.Phone, r.Code, m.Status != MembershipStatus.Active)).ToListAsync(ct);

    public async Task<List<DeviceListItem>> Devices(Guid shopId, CancellationToken ct)
    {
        var rows = await db.Devices.AsNoTracking().Where(d => d.ShopId == shopId).ToListAsync(ct);
        return rows.Select(d => new DeviceListItem(d.Id, d.Name, d.Status.ToString().ToLowerInvariant(), d.LastSeenAt, d.WipeRequested)).ToList();
    }
}
