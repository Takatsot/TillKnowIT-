namespace ShopCore.Contracts;

public sealed record MonthlySales(string Month, long SalesCents, int Sales);

/// <summary>A shop's sales by month, for partners the owner has agreed to share with. Callers must check the owner's consent before they ask.</summary>
public interface ISalesHistory
{
    /// <summary>The last full months and this one so far, oldest first. Cancelled sales are left out.</summary>
    Task<List<MonthlySales>> Months(Guid shopId, int months, CancellationToken ct);
}
