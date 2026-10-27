using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Catalogue;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

internal sealed class ProductRepointStore(ShopCoreDbContext db) : IProductRepointStore
{
    // Bumping the version and time makes the change reach the shops' devices like any other product edit.
    public Task<int> Repoint(Guid fromCatalogueProductId, Guid toCatalogueProductId, DateTimeOffset now, CancellationToken ct) =>
        db.Products.Where(p => p.CatalogueProductId == fromCatalogueProductId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.CatalogueProductId, toCatalogueProductId)
                .SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.Version, p => p.Version + 1), ct);
}
