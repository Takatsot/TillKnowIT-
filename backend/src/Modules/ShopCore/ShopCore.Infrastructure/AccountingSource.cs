using Microsoft.EntityFrameworkCore;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.SharedKernel;

namespace ShopCore.Infrastructure;

internal sealed class AccountingSource(ShopCoreDbContext db) : IDailyAccountingSource
{
    public async Task<DayFigures> Day(Guid shopId, DateOnly day, CancellationToken ct)
    {
        var from = BusinessDay.Start(day);
        var to = BusinessDay.Start(day.AddDays(1));
        var sales = await db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Payments)
            .Where(s => s.ShopId == shopId && s.SoldAt >= from && s.SoldAt < to && !db.Set<SaleCancellation>().Any(c => c.SaleId == s.Id)).ToListAsync(ct);

        long cash = 0, card = 0, credit = 0, other = 0;
        void Add(string method, long cents)
        {
            switch (method)
            {
                case PaymentMethods.Cash: cash += cents; break;
                case PaymentMethods.Card or PaymentMethods.Qr: card += cents; break;
                case PaymentMethods.Credit: credit += cents; break;
                default: other += cents; break;
            }
        }
        foreach (var s in sales)
        {
            if (s.PaymentMethodCode == PaymentMethods.Split) foreach (var p in s.Payments) Add(p.MethodCode, p.AmountCents);
            else Add(s.PaymentMethodCode, s.TotalCents);
        }

        var cost = sales.SelectMany(s => s.Lines).Sum(l => SaleMath.LineGross(l.CostCents, l.Quantity, l.UnitDivisor));
        var purchases = await db.StockMovements.AsNoTracking()
            .Where(m => m.ShopId == shopId && m.Type == StockMovementType.Receive && m.CreatedAt >= from && m.CreatedAt < to && m.UnitCostCents != null)
            .SumAsync(m => (long?)(m.Quantity * m.UnitCostCents!.Value), ct) ?? 0;
        return new DayFigures(day, sales.Count, cash, card, credit, other, sales.Sum(s => s.VatCents), cost, purchases);
    }
}
