using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Credit;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

internal sealed class CreditReadStore(ShopCoreDbContext db) : ICreditReadStore
{
    public async Task<List<(Guid Id, string Name, string? Phone, long? LimitCents, long BalanceCents)>> Balances(Guid shopId, CancellationToken ct)
    {
        var rows = await db.Set<CreditAccount>().AsNoTracking()
            .Where(a => a.ShopId == shopId)
            .Select(a => new
            {
                a.Id, a.Name, a.Phone, a.CreditLimitCents,
                Balance = db.Set<CreditEntry>().Where(e => e.AccountId == a.Id).Sum(e => (long?)e.AmountCents) ?? 0,
            })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Name, r.Phone, r.CreditLimitCents, r.Balance)).ToList();
    }

    public Task<bool> AccountExists(Guid shopId, Guid accountId, CancellationToken ct) =>
        db.Set<CreditAccount>().AsNoTracking().AnyAsync(a => a.Id == accountId && a.ShopId == shopId, ct);

    public async Task<List<CreditHistoryEntry>> History(Guid accountId, CancellationToken ct)
    {
        var rows = await db.Set<CreditEntry>().AsNoTracking()
            .Where(e => e.AccountId == accountId)
            .Select(e => new { e.Id, e.Type, e.AmountCents, e.SaleId, e.PaymentMethodCode, e.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(r => new CreditHistoryEntry(r.Id, r.Type.ToString().ToLowerInvariant(), r.AmountCents, r.SaleId, r.PaymentMethodCode, r.CreatedAt)).ToList();
    }
}
