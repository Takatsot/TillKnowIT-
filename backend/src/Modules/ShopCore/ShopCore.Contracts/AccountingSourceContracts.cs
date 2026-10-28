namespace ShopCore.Contracts;

/// <summary>
/// A shop's money for one business day, for the accountant. Sales are what customers paid (VAT included) split by how; cancelled sales are left out.
/// </summary>
public sealed record DayFigures(DateOnly Day, int Sales, long CashCents, long CardCents, long CreditCents, long OtherCents, long VatCents, long CostOfSalesCents, long PurchasesCents)
{
    public long GrossCents => CashCents + CardCents + CreditCents + OtherCents;
}

public interface IDailyAccountingSource
{
    Task<DayFigures> Day(Guid shopId, DateOnly day, CancellationToken ct);
}
