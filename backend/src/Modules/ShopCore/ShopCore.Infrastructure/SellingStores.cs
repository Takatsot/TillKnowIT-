using Microsoft.EntityFrameworkCore;
using Platform.Contracts;
using ShopCore.Application.Sync;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

/// <summary>The sale flow asks about features through this; it is the only place ShopCore touches the Platform module.</summary>
internal sealed class FeatureGate(IShopFeatures features) : IFeatureGate
{
    public Task<bool> IsOn(Guid shopId, string feature, CancellationToken ct) => features.IsOn(shopId, feature, ct);

    public async Task<string?> Settings(Guid shopId, string feature, CancellationToken ct) =>
        (await features.For(shopId, ct)).Features.FirstOrDefault(f => f.Flag == feature)?.SettingsJson;
}

internal sealed class RefundStore(ShopCoreDbContext db) : IRefundStore
{
    public async Task<HashSet<Guid>> ExistingIds(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return (await db.Set<SaleRefund>().AsNoTracking().Where(r => list.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task<Dictionary<Guid, List<SoldLine>>> SoldLines(Guid shopId, IEnumerable<Guid> saleIds, CancellationToken ct)
    {
        var ids = saleIds.ToList();
        var rows = await db.Sales.AsNoTracking().Where(s => s.ShopId == shopId && ids.Contains(s.Id))
            .SelectMany(s => s.Lines.Select(l => new { SaleId = s.Id, l.ProductId, l.Name, l.Quantity, l.PriceCents, l.UnitDivisor, l.DiscountCents })).ToListAsync(ct);
        return rows.GroupBy(r => r.SaleId).ToDictionary(g => g.Key,
            g => g.Select(r => new SoldLine(r.ProductId, r.ProductId, r.Name, r.Quantity, SaleMath.LineTotal(r.PriceCents, r.Quantity, r.UnitDivisor, r.DiscountCents))).ToList());
    }

    public async Task<List<(Guid SaleId, Guid ProductId, int Quantity)>> RefundedSoFar(IEnumerable<Guid> saleIds, CancellationToken ct)
    {
        var ids = saleIds.ToList();
        var rows = await db.Set<SaleRefund>().AsNoTracking().Where(r => ids.Contains(r.SaleId)).SelectMany(r => r.Lines.Select(l => new { r.SaleId, l.ProductId, l.Quantity })).ToListAsync(ct);
        return rows.Select(r => (r.SaleId, r.ProductId, r.Quantity)).ToList();
    }

    public async Task<long> CashRefunds(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await db.Set<SaleRefund>().AsNoTracking().Where(r => r.DeviceId == deviceId && r.MethodCode == PaymentMethods.Cash && r.RefundedAt >= from && r.RefundedAt <= to)
            .SumAsync(r => (long?)r.AmountCents, ct) ?? 0;

    public async Task<long> CashIn(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var layby = await db.Set<LaybyPayment>().AsNoTracking().Where(p => p.DeviceId == deviceId && p.MethodCode == PaymentMethods.Cash && p.PaidAt >= from && p.PaidAt <= to).SumAsync(p => (long?)p.AmountCents, ct) ?? 0;
        var vouchers = await db.Set<GiftVoucher>().AsNoTracking().Where(v => v.DeviceId == deviceId && v.PaidWithMethod == PaymentMethods.Cash && v.CreatedAt >= from && v.CreatedAt <= to).SumAsync(v => (long?)v.InitialCents, ct) ?? 0;
        return layby + vouchers;
    }

    public void Add(SaleRefund refund) => db.Set<SaleRefund>().Add(refund);
}
