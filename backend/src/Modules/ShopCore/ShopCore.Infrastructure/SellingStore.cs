using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Selling;
using ShopCore.Application.Sync;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

internal sealed class SellingStore(ShopCoreDbContext db) : ISellingStore
{
    private DbSet<T> Set<T>() where T : class => db.Set<T>();

    // ---- products and variants ----
    public Task<Product?> GetProduct(Guid shopId, Guid productId, CancellationToken ct) => db.Products.FirstOrDefaultAsync(p => p.ShopId == shopId && p.Id == productId, ct);
    public Task<List<Product>> ProductsByIds(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken ct) => db.Products.Where(p => p.ShopId == shopId && ids.Contains(p.Id)).ToListAsync(ct);
    public Task<bool> BarcodeTaken(Guid shopId, string barcode, CancellationToken ct) => db.Products.AsNoTracking().AnyAsync(p => p.ShopId == shopId && p.Barcode == barcode, ct);
    public Task<List<Product>> VariantsOf(Guid parentProductId, CancellationToken ct) => db.Products.Where(p => p.ParentProductId == parentProductId).ToListAsync(ct);
    public void Add(Product product) => db.Products.Add(product);

    // ---- modifiers ----
    public void Add(ModifierGroup group) => Set<ModifierGroup>().Add(group);
    public Task<ModifierGroup?> GetGroup(Guid shopId, Guid groupId, CancellationToken ct) => Set<ModifierGroup>().Include(g => g.Options).FirstOrDefaultAsync(g => g.ShopId == shopId && g.Id == groupId, ct);
    public Task<List<ModifierGroup>> Groups(Guid shopId, CancellationToken ct) => Set<ModifierGroup>().Include(g => g.Options).Where(g => g.ShopId == shopId).ToListAsync(ct);
    public Task<List<ModifierGroup>> GroupsByIds(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken ct) => Set<ModifierGroup>().Include(g => g.Options).Where(g => g.ShopId == shopId && ids.Contains(g.Id)).ToListAsync(ct);
    public Task<List<Guid>> GroupIdsOfProduct(Guid productId, CancellationToken ct) => Set<ProductModifierGroup>().AsNoTracking().Where(l => l.ProductId == productId).OrderBy(l => l.SortOrder).Select(l => l.GroupId).ToListAsync(ct);

    public async Task ReplaceProductGroups(Guid productId, IReadOnlyList<Guid> groupIds, CancellationToken ct)
    {
        Set<ProductModifierGroup>().RemoveRange(await Set<ProductModifierGroup>().Where(l => l.ProductId == productId).ToListAsync(ct));
        for (var i = 0; i < groupIds.Count; i++) Set<ProductModifierGroup>().Add(new ProductModifierGroup(productId, groupIds[i], i));
    }

    // ---- promotions and the floor plan ----
    public void Add(Promotion promotion) => Set<Promotion>().Add(promotion);
    public Task<Promotion?> GetPromotion(Guid shopId, Guid promotionId, CancellationToken ct) => Set<Promotion>().FirstOrDefaultAsync(p => p.ShopId == shopId && p.Id == promotionId, ct);
    public Task<List<Promotion>> Promotions(Guid shopId, CancellationToken ct) => Set<Promotion>().Where(p => p.ShopId == shopId).ToListAsync(ct);
    public Task<List<FloorTable>> Tables(Guid shopId, CancellationToken ct) => Set<FloorTable>().Where(t => t.ShopId == shopId).ToListAsync(ct);
    public void Add(FloorTable table) => Set<FloorTable>().Add(table);

    // ---- tickets and the kitchen ----
    public void Add(OpenTicket ticket) => Set<OpenTicket>().Add(ticket);
    public Task<OpenTicket?> GetTicket(Guid shopId, Guid ticketId, CancellationToken ct) => Set<OpenTicket>().FirstOrDefaultAsync(t => t.ShopId == shopId && t.Id == ticketId, ct);
    public Task<List<OpenTicket>> OpenTickets(Guid shopId, CancellationToken ct) => Set<OpenTicket>().Where(t => t.ShopId == shopId && t.Status == TicketStatus.Open).ToListAsync(ct);
    public void Add(KitchenOrder order) => Set<KitchenOrder>().Add(order);
    public Task<KitchenOrder?> GetKitchenOrder(Guid shopId, Guid orderId, CancellationToken ct) => Set<KitchenOrder>().FirstOrDefaultAsync(o => o.ShopId == shopId && o.Id == orderId, ct);
    public Task<KitchenOrder?> KitchenOrderByNo(Guid shopId, string orderNo, CancellationToken ct) => Set<KitchenOrder>().FirstOrDefaultAsync(o => o.ShopId == shopId && o.OrderNo == orderNo, ct);

    public Task<List<KitchenOrder>> KitchenOrders(Guid shopId, IReadOnlyCollection<KitchenStatus> statuses, DateTimeOffset since, CancellationToken ct) =>
        Set<KitchenOrder>().AsNoTracking().Where(o => o.ShopId == shopId && statuses.Contains(o.Status) && o.CreatedAt >= since).ToListAsync(ct);

    // ---- quotes and lay-by ----
    public void Add(Quote quote) => Set<Quote>().Add(quote);
    public Task<Quote?> GetQuote(Guid shopId, Guid quoteId, CancellationToken ct) => Set<Quote>().FirstOrDefaultAsync(q => q.ShopId == shopId && q.Id == quoteId, ct);
    public Task<List<Quote>> Quotes(Guid shopId, CancellationToken ct) => Set<Quote>().AsNoTracking().Where(q => q.ShopId == shopId).ToListAsync(ct);
    public void Add(LaybyPlan plan) => Set<LaybyPlan>().Add(plan);
    public Task<LaybyPlan?> GetLayby(Guid shopId, Guid planId, CancellationToken ct) => Set<LaybyPlan>().Include(p => p.Payments).FirstOrDefaultAsync(p => p.ShopId == shopId && p.Id == planId, ct);
    public Task<List<LaybyPlan>> Laybys(Guid shopId, CancellationToken ct) => Set<LaybyPlan>().Include(p => p.Payments).Where(p => p.ShopId == shopId).ToListAsync(ct);

    public Task<List<LaybyPlan>> ActiveLaybysPastDue(DateOnly today, int take, CancellationToken ct) =>
        Set<LaybyPlan>().Include(p => p.Payments).Where(p => p.Status == LaybyStatus.Active && p.DueOn < today).OrderBy(p => p.DueOn).Take(take).ToListAsync(ct);

    // ---- vouchers and serials ----
    public void Add(GiftVoucher voucher) => Set<GiftVoucher>().Add(voucher);
    public Task<GiftVoucher?> VoucherByHash(Guid shopId, string codeHash, CancellationToken ct) => Set<GiftVoucher>().Include(v => v.Entries).FirstOrDefaultAsync(v => v.ShopId == shopId && v.CodeHash == codeHash, ct);
    public void Add(SerialNumberRecord serial) => Set<SerialNumberRecord>().Add(serial);
    public Task<bool> SerialExists(Guid shopId, Guid productId, string serial, CancellationToken ct) => Set<SerialNumberRecord>().AsNoTracking().AnyAsync(s => s.ShopId == shopId && s.ProductId == productId && s.Serial == serial, ct);

    public Task<List<SerialNumberRecord>> FindSerials(Guid shopId, string text, int take, CancellationToken ct)
    {
        var like = text.Trim().ToUpperInvariant();
        return Set<SerialNumberRecord>().AsNoTracking().Where(s => s.ShopId == shopId && s.Serial.Contains(like)).OrderByDescending(s => s.SoldAt).Take(take).ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, int>> SoldQuantities(Guid shopId, Guid saleId, CancellationToken ct) =>
        await db.Sales.AsNoTracking().Where(s => s.ShopId == shopId && s.Id == saleId).SelectMany(s => s.Lines).ToDictionaryAsync(l => l.ProductId, l => l.Quantity, ct);

    public async Task<Dictionary<Guid, int>> SerialCounts(Guid shopId, Guid saleId, CancellationToken ct) =>
        await Set<SerialNumberRecord>().AsNoTracking().Where(s => s.ShopId == shopId && s.SaleId == saleId).GroupBy(s => s.ProductId).ToDictionaryAsync(g => g.Key, g => g.Count(), ct);

    // ---- online refunds ----
    public async Task<SaleSummary?> SaleSummary(Guid shopId, Guid saleId, CancellationToken ct)
    {
        var s = await db.Sales.AsNoTracking().Where(x => x.ShopId == shopId && x.Id == saleId).Select(x => new { x.PaymentMethodCode, x.AccountId, x.TotalCents }).FirstOrDefaultAsync(ct);
        return s is null ? null : new SaleSummary(s.PaymentMethodCode, s.AccountId, s.TotalCents);
    }

    public Task<bool> SaleCancelled(Guid saleId, CancellationToken ct) => Set<SaleCancellation>().AsNoTracking().AnyAsync(c => c.SaleId == saleId, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
