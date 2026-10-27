namespace ShopCore.Domain;

public sealed record StockItem(
    Guid ProductId, string Name, string? Barcode, bool IsActive, DateTimeOffset AddedAt,
    int OnHand, int LowStockLevel, DateOnly? ExpiryDate, int SoldLast30Days, DateTimeOffset? LastSoldAt, long PriceCents = 0);

public enum StockFilter { All, RunningLow, Out, NotSelling, Expiring }

/// <summary>
/// The shared stock rules behind the stock list, the insights and the reports. The till has the same rules in
/// TypeScript (packages/domain stockRules.ts); the numbers below must stay in step.
/// </summary>
public static class StockRules
{
    public const int NotSellingDays = 30;
    public const int ExpiringSoonDays = 14;
    public const int PlanningDays = 14;

    /// <summary>Nothing on the shelf (or less than nothing, which also means a count is due).</summary>
    public static bool IsOut(StockItem i) => i.OnHand <= 0;

    /// <summary>Some left, but at or below the level the owner set. A level of 0 means "no alert".</summary>
    public static bool IsRunningLow(StockItem i) => i.OnHand > 0 && i.LowStockLevel > 0 && i.OnHand <= i.LowStockLevel;

    /// <summary>Stock sitting on the shelf with no sale for 30 days. A product added in the last 30 days gets the benefit of the doubt.</summary>
    public static bool IsNotSelling(StockItem i, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-NotSellingDays);
        return i.OnHand > 0 && i.AddedAt <= cutoff && (i.LastSoldAt is null || i.LastSoldAt < cutoff);
    }

    public static bool IsExpired(StockItem i, DateOnly today) => i.ExpiryDate is { } e && e < today;

    /// <summary>Stock that expires within 14 days, or already has. Only matters while there is stock.</summary>
    public static bool IsExpiring(StockItem i, DateOnly today) =>
        i.OnHand > 0 && i.ExpiryDate is { } e && e <= today.AddDays(ExpiringSoonDays);

    /// <summary>Negative stock is allowed so a sale is never blocked, but it means a count is due.</summary>
    public static bool NeedsCount(StockItem i) => i.OnHand < 0;

    public static double DailySalesRate(StockItem i) => i.SoldLast30Days / 30.0;

    /// <summary>How many days the stock will last at the recent selling rate, or null when nothing is selling or nothing is left.</summary>
    public static int? DaysLeft(StockItem i)
    {
        var rate = DailySalesRate(i);
        return i.OnHand > 0 && rate > 0 ? (int)Math.Floor(i.OnHand / rate) : null;
    }

    /// <summary>What to order to cover the next two weeks of selling, never below zero.</summary>
    public static int SuggestedOrderQuantity(StockItem i) =>
        Math.Max(0, (int)Math.Ceiling(DailySalesRate(i) * PlanningDays) - Math.Max(i.OnHand, 0));

    public static bool Matches(StockItem i, StockFilter filter, DateTimeOffset now, DateOnly today) => filter switch
    {
        StockFilter.RunningLow => IsRunningLow(i),
        StockFilter.Out => IsOut(i),
        StockFilter.NotSelling => IsNotSelling(i, now),
        StockFilter.Expiring => IsExpiring(i, today),
        _ => true,
    };

    public static List<string> Flags(StockItem i, DateTimeOffset now, DateOnly today)
    {
        var flags = new List<string>();
        if (IsOut(i)) flags.Add("out");
        if (IsRunningLow(i)) flags.Add("low");
        if (IsNotSelling(i, now)) flags.Add("not_selling");
        if (IsExpired(i, today)) flags.Add("expired");
        else if (IsExpiring(i, today)) flags.Add("expiring");
        if (NeedsCount(i)) flags.Add("needs_count");
        return flags;
    }
}

public static class StockCounts
{
    public const string CountedReason = "counted";

    /// <summary>
    /// Turns a count into the movement that fixes the books, or null when nothing differs.
    /// A shortfall with a loss reason (expired, damaged, missing, own use) is a write-off; anything else is a count.
    /// </summary>
    public static (StockMovementType Type, int Quantity)? FromCount(int onHand, int counted, string reasonCode)
    {
        var difference = counted - onHand;
        if (difference == 0) return null;
        return difference < 0 && reasonCode != CountedReason
            ? (StockMovementType.WriteOff, difference)
            : (StockMovementType.Count, difference);
    }
}
