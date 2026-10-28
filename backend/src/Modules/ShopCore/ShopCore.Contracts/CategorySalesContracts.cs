namespace ShopCore.Contracts;

public sealed record CategoryDay(string CategoryCode, int Units, long RevenueCents);

/// <summary>What a shop sold on one business day by product category. Only the market insights job reads this, and only for shops that opted in.</summary>
public interface ICategorySalesSource
{
    Task<List<CategoryDay>> Day(Guid shopId, DateOnly day, CancellationToken ct);
}
