using Marketplace.Application.Suppliers;
using Marketplace.Domain;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure;

internal sealed class SupplierStore(MarketplaceDbContext db) : ISupplierStore
{
    public async Task Add(Supplier supplier, CancellationToken ct) => await db.Set<Supplier>().AddAsync(supplier, ct);

    public Task<Supplier?> Get(Guid supplierId, CancellationToken ct) => db.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == supplierId, ct);

    public Task<List<Supplier>> Pending(CancellationToken ct) =>
        db.Set<Supplier>().AsNoTracking().Where(s => s.Status == SupplierStatus.Pending).ToListAsync(ct);

    public Task<List<SupplierArea>> Areas(Guid supplierId, CancellationToken ct) =>
        db.Set<SupplierArea>().AsNoTracking().Where(a => a.SupplierId == supplierId).ToListAsync(ct);

    public async Task ReplaceAreas(Guid supplierId, List<SupplierArea> areas, CancellationToken ct)
    {
        var old = await db.Set<SupplierArea>().Where(a => a.SupplierId == supplierId).ToListAsync(ct);
        db.Set<SupplierArea>().RemoveRange(old);
        await db.Set<SupplierArea>().AddRangeAsync(areas, ct);
    }

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class SupplierProductStore(MarketplaceDbContext db) : ISupplierProductStore
{
    public async Task<Dictionary<string, SupplierProduct>> ByBarcode(Guid supplierId, CancellationToken ct) =>
        await db.Set<SupplierProduct>().Where(p => p.SupplierId == supplierId && p.Barcode != null).ToDictionaryAsync(p => p.Barcode!, ct);

    public Task<SupplierProduct?> Get(Guid productId, CancellationToken ct) => db.Set<SupplierProduct>().FirstOrDefaultAsync(p => p.Id == productId, ct);

    public Task<List<SupplierProduct>> List(Guid supplierId, CancellationToken ct) =>
        db.Set<SupplierProduct>().AsNoTracking().Where(p => p.SupplierId == supplierId).ToListAsync(ct);

    public async Task Add(SupplierProduct product, CancellationToken ct) => await db.Set<SupplierProduct>().AddAsync(product, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
