using Marketplace.Application.Orders;
using Marketplace.Domain;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure;

internal sealed class OrderStore(MarketplaceDbContext db) : IOrderStore
{
    public async Task Add(PurchaseOrder order, CancellationToken ct) => await db.Set<PurchaseOrder>().AddAsync(order, ct);

    public Task<PurchaseOrder?> Get(Guid orderId, CancellationToken ct) =>
        db.Set<PurchaseOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct);

    public Task<List<PurchaseOrder>> ForShop(Guid shopId, CancellationToken ct) =>
        db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines).Where(o => o.ShopId == shopId).ToListAsync(ct);

    public Task<List<PurchaseOrder>> ForSupplier(Guid supplierId, OrderStatus? status, CancellationToken ct) =>
        db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines).Where(o => o.SupplierId == supplierId && (status == null || o.Status == status)).ToListAsync(ct);

    public Task<LastPrice?> GetLastPrice(Guid shopId, Guid supplierId, string barcode, CancellationToken ct) =>
        db.Set<LastPrice>().FirstOrDefaultAsync(p => p.ShopId == shopId && p.SupplierId == supplierId && p.Barcode == barcode, ct);

    public async Task Add(LastPrice price, CancellationToken ct) => await db.Set<LastPrice>().AddAsync(price, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
