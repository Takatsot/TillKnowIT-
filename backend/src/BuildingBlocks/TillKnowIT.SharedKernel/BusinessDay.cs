namespace TillKnowIT.SharedKernel;

/// <summary>Shops trade in South Africa, which has no daylight saving, so a business day is a fixed UTC+2 day.</summary>
public static class BusinessDay
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    public static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), Offset);

    public static DateOnly Of(DateTimeOffset moment) => DateOnly.FromDateTime(moment.ToOffset(Offset).DateTime);
}
