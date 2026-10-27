using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public static class CashUpCalculator
{
    /// <summary>Float + cash sales + cash paid on credit accounts - cash given back in refunds. Cancelled sales are not in the cash sales.</summary>
    public static long Expected(long floatCents, long cashSalesCents, long creditCashPaymentsCents, long cashRefundsCents = 0) =>
        floatCents + cashSalesCents + creditCashPaymentsCents - cashRefundsCents;
}

public sealed record CashUpInput(
    Guid Id, Guid ShopId, Guid DeviceId, Guid StaffId, DateOnly Day, long FloatCents, long CountedCents,
    string? Note, DateTimeOffset CashedUpAt);

/// <summary>One per till per day. Over or short is counted minus expected; the server works out expected itself.</summary>
public sealed class CashUp : Entity
{
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromHours(24);

    private CashUp() { }

    public static Result<CashUpInput> Check(CashUpInput i, DateTimeOffset now)
    {
        if (i.FloatCents < 0 || i.CountedCents < 0)
            return Result<CashUpInput>.Fail("cashup.amount_invalid", "Float and counted cash cannot be negative.");
        if (i.CashedUpAt > now + MaxClockSkew)
            return Result<CashUpInput>.Fail("cashup.date_invalid", "The cash-up is dated in the future.");
        if (BusinessDay.Of(i.CashedUpAt) != i.Day)
            return Result<CashUpInput>.Fail("cashup.day_invalid", "The cash-up time is not on the day it is for.");
        if (i.Note is not null && i.Note.Length > 500)
            return Result<CashUpInput>.Fail("cashup.note_invalid", "The note is too long.");
        return Result<CashUpInput>.Ok(i);
    }

    public static CashUp Record(CashUpInput i, long expectedCents)
    {
        var cashUp = new CashUp
        {
            ShopId = i.ShopId,
            TillDeviceId = i.DeviceId,
            Day = i.Day,
            FloatCents = i.FloatCents,
            ExpectedCents = expectedCents,
            CountedCents = i.CountedCents,
            DifferenceCents = i.CountedCents - expectedCents,
            Note = string.IsNullOrWhiteSpace(i.Note) ? null : i.Note.Trim(),
            CashedUpAt = i.CashedUpAt,
        };
        cashUp.StampFromDevice(i.Id, i.CashedUpAt, i.StaffId, i.DeviceId);
        return cashUp;
    }

    public Guid ShopId { get; private set; }
    public Guid TillDeviceId { get; private set; }
    public DateOnly Day { get; private set; }
    public long FloatCents { get; private set; }
    public long ExpectedCents { get; private set; }
    public long CountedCents { get; private set; }

    /// <summary>Counted minus expected. Negative means the till is short.</summary>
    public long DifferenceCents { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CashedUpAt { get; private set; }
}
