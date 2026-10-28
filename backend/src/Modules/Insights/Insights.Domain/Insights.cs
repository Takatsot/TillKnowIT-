using TillKnowIT.SharedKernel;

namespace Insights.Domain;

/// <summary>The anonymity rule: a figure is only ever published when at least this many different shops are behind it.</summary>
public static class Anonymity
{
    public const int MinShops = 10;

    public static bool CanPublish(int shopCount) => shopCount >= MinShops;
}

/// <summary>G5-1: whether a shop's anonymised figures are included in market insights. Off until the owner turns it on, and the owner can change it at any time.</summary>
public sealed class InsightOptIn : VersionedEntity
{
    private InsightOptIn() { }

    public Guid ShopId { get; private set; }
    public bool OptedIn { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public Guid ChangedBy { get; private set; }

    public static InsightOptIn Create(Guid shopId, bool optedIn, Guid personId, DateTimeOffset now)
    {
        var o = new InsightOptIn { ShopId = shopId, OptedIn = optedIn, ChangedAt = now, ChangedBy = personId };
        o.StampFromDevice(Guid.CreateVersion7(), now, personId, null);
        return o;
    }

    public void Set(bool optedIn, Guid personId, DateTimeOffset now)
    {
        if (OptedIn == optedIn) return;
        OptedIn = optedIn;
        ChangedAt = now;
        ChangedBy = personId;
        Touch();
    }
}

/// <summary>
/// One published figure: what was sold in a product category in an area on a day, by at least ten shops together.
/// There is no shop, person, device or customer on this row, so it cannot lead back to anyone.
/// </summary>
public sealed class AreaAggregate : Entity
{
    private AreaAggregate() { }

    public string Area { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public DateOnly Day { get; private set; }
    public int Units { get; private set; }
    public long RevenueCents { get; private set; }
    public int ShopCount { get; private set; }

    public AreaAggregate(string area, string category, DateOnly day, int units, long revenueCents, int shopCount, DateTimeOffset now)
    {
        Area = area;
        Category = category;
        Day = day;
        Units = units;
        RevenueCents = revenueCents;
        ShopCount = shopCount;
        CreatedAt = now;
    }
}

/// <summary>One shop's sales of a category on a day. It is used inside the aggregation and never stored.</summary>
public sealed record ShopDay(Guid ShopId, string Area, string Category, int Units, long RevenueCents);

public static class Aggregator
{
    /// <summary>Adds shops together per area and category. Anything made from fewer than ten shops is left out entirely, not shown as a small number.</summary>
    public static List<AreaAggregate> Build(IEnumerable<ShopDay> rows, DateOnly day, DateTimeOffset now) =>
        rows.GroupBy(r => (Area: r.Area.Trim().ToLowerInvariant(), Category: r.Category.Trim().ToLowerInvariant()))
            .Select(g => (g.Key, Shops: g.Select(r => r.ShopId).Distinct().Count(), Units: g.Sum(r => r.Units), Revenue: g.Sum(r => r.RevenueCents)))
            .Where(g => Anonymity.CanPublish(g.Shops))
            .OrderBy(g => g.Key.Area).ThenBy(g => g.Key.Category)
            .Select(g => new AreaAggregate(g.Key.Area, g.Key.Category, day, g.Units, g.Revenue, g.Shops, now))
            .ToList();

    /// <summary>Revenue is shown as a band, not an exact figure, so a published number says less than the data behind it.</summary>
    public static string RevenueBand(long cents) => cents switch
    {
        < 100_000 => "under R1 000",
        < 500_000 => "R1 000 to R5 000",
        < 2_500_000 => "R5 000 to R25 000",
        < 10_000_000 => "R25 000 to R100 000",
        _ => "over R100 000",
    };
}
