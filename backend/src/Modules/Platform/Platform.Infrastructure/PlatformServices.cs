using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.Contracts;
using Platform.Domain;
using Platform.Infrastructure.Persistence;

namespace Platform.Infrastructure;

public static partial class PlatformModule
{
    static partial void AddServices(IServiceCollection services)
    {
        services.AddScoped<IPackStore, PackStore>();
        services.AddScoped<IShopFeatures, ShopFeatures>();
    }
}

internal sealed class PackStore(PlatformDbContext db) : IPackStore
{
    public async Task Add(BusinessPack pack, CancellationToken ct) => await db.Set<BusinessPack>().AddAsync(pack, ct);
    public Task<BusinessPack?> Get(string code, CancellationToken ct) => db.Set<BusinessPack>().Include(p => p.Features).FirstOrDefaultAsync(p => p.Code == code, ct);

    public Task<List<BusinessPack>> List(bool activeOnly, CancellationToken ct) =>
        db.Set<BusinessPack>().AsNoTracking().Include(p => p.Features).Where(p => !activeOnly || p.IsActive).ToListAsync(ct);

    public Task<ShopPack?> ShopPack(Guid shopId, CancellationToken ct) => db.Set<ShopPack>().FirstOrDefaultAsync(s => s.ShopId == shopId, ct);
    public async Task Add(ShopPack shopPack, CancellationToken ct) => await db.Set<ShopPack>().AddAsync(shopPack, ct);
    public async Task Add(ShopPackChange change, CancellationToken ct) => await db.Set<ShopPackChange>().AddAsync(change, ct);
    public Task<int> ShopsOn(string packCode, CancellationToken ct) => db.Set<ShopPack>().CountAsync(s => s.PackCode == packCode, ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
