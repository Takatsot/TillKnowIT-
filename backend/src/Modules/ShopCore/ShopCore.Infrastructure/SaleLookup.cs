using Microsoft.EntityFrameworkCore;
using ShopCore.Contracts;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

internal sealed class SaleLookup(ShopCoreDbContext db) : ISaleLookup
{
    public Task<bool> Recorded(Guid saleId, CancellationToken ct) => db.Sales.AsNoTracking().AnyAsync(s => s.Id == saleId, ct);
}
