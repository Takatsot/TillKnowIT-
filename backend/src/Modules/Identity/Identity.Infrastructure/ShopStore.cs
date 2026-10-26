using Identity.Application.Shops;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

internal sealed class ShopStore(IdentityDbContext db) : IShopStore
{
    public async Task Add(Organisation shop, CancellationToken ct) => await db.Organisations.AddAsync(shop, ct);

    public Task<bool> PersonExists(Guid personId, CancellationToken ct) => db.People.AnyAsync(p => p.Id == personId, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
