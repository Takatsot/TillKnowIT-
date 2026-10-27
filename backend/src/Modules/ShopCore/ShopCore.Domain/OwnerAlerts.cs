using TillKnowIT.SharedKernel;
using System.Globalization;

namespace ShopCore.Domain;

/// <summary>
/// The owner's alert levels. They live in the shop's settings table under these keys, so an owner changes them without a release.
/// A value that is missing, not a whole number, or not above zero falls back to the default.
/// </summary>
public sealed record AlertSettings(int CancellationsPerHour, long CashUpShortCents)
{
    public const string CancellationsKey = "alert.cancellations_per_hour";
    public const string CashUpShortKey = "alert.cashup_short_cents";
    public static readonly string[] Keys = [CancellationsKey, CashUpShortKey];

    public static AlertSettings Defaults { get; } = new(3, 5000);

    public static AlertSettings From(IReadOnlyDictionary<string, string> settings) => new(
        settings.TryGetValue(CancellationsKey, out var c) && int.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out var cancellations) && cancellations > 0
            ? cancellations : Defaults.CancellationsPerHour,
        settings.TryGetValue(CashUpShortKey, out var s) && long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var shortCents) && shortCents > 0
            ? shortCents : Defaults.CashUpShortCents);
}

/// <summary>E11-2: unusual activity worth a message to the owner.</summary>
public static class LossAlertRules
{
    /// <summary>The limit is reached when that many sales were cancelled in the last hour.</summary>
    public static bool TooManyCancellations(IEnumerable<DateTimeOffset> cancelledAt, DateTimeOffset now, int limit) =>
        cancelledAt.Count(t => t > now.AddHours(-1) && t <= now) >= limit;

    /// <summary>Short by at least the threshold. Over is never an alert here: the owner sees it in the cash-up report.</summary>
    public static bool CashUpTooShort(long differenceCents, long thresholdCents) => differenceCents <= -thresholdCents;

    /// <summary>One alert per shop per hour, so a burst of cancellations is one message and not twenty.</summary>
    public static string CancellationKey(Guid shopId, DateTimeOffset now) => $"alert:cancel:{shopId:N}:{now.ToUniversalTime():yyyyMMddHH}";

    public static string CashUpKey(Guid cashUpId) => $"alert:cashup:{cashUpId:N}";

    /// <summary>One alert per outage: the key follows the last time the till was seen, so a later outage alerts again.</summary>
    public static string OfflineKey(Guid deviceId, DateTimeOffset lastSeenAt) => $"alert:offline:{deviceId:N}:{lastSeenAt.ToUniversalTime():yyyyMMddHHmm}";

    public const int OfflineAlertHours = 48;
}

/// <summary>E11-1: the evening summary goes out once a day, after the shop has closed.</summary>
public static class DailySummaryRules
{
    public static readonly TimeOnly SendAfter = new(19, 0);

    public static bool IsDue(DateTimeOffset now) => TimeOnly.FromDateTime(now.ToOffset(BusinessDay.Offset).DateTime) >= SendAfter;

    public static string Key(Guid shopId, DateOnly day) => $"daily:{shopId:N}:{day:yyyy-MM-dd}";

    /// <summary>The cash-up line of the summary in plain words.</summary>
    public static string CashUpText(IReadOnlyCollection<long> differencesCents)
    {
        if (differencesCents.Count == 0) return "not done today";
        var total = differencesCents.Sum();
        var rand = (Math.Abs(total) / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return total switch { 0 => "balanced", < 0 => $"short by R{rand}", _ => $"over by R{rand}" };
    }
}
