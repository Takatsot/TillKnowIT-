using TillKnowIT.SharedKernel;

namespace Bookings.Domain;

/// <summary>B5-1: something the shop sells by the hour: a haircut, a manicure. Duration is in minutes, in steps of five.</summary>
public sealed class ServiceItem : VersionedEntity
{
    private ServiceItem() { }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = default!;
    public int DurationMinutes { get; private set; }
    public long PriceCents { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Result<ServiceItem> Create(Guid shopId, string? name, int durationMinutes, long priceCents, DateTimeOffset now)
    {
        var valid = Validate(name, durationMinutes, priceCents);
        if (!valid.IsSuccess) return Result<ServiceItem>.Fail(valid.Error!.Code, valid.Error.Message);
        return Result<ServiceItem>.Ok(new ServiceItem { ShopId = shopId, Name = name!.Trim(), DurationMinutes = durationMinutes, PriceCents = priceCents, CreatedAt = now });
    }

    public Result Change(string? name, int durationMinutes, long priceCents, bool active)
    {
        var valid = Validate(name, durationMinutes, priceCents);
        if (!valid.IsSuccess) return valid;
        Name = name!.Trim();
        DurationMinutes = durationMinutes;
        PriceCents = priceCents;
        IsActive = active;
        Touch();
        return Result.Ok();
    }

    private static Result Validate(string? name, int minutes, long price)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return Result.Fail("service.name_invalid", "Give the service a name of up to 100 characters.");
        if (minutes is < 5 or > 480 || minutes % 5 != 0) return Result.Fail("service.duration_invalid", "A service takes from 5 minutes to 8 hours, in steps of 5 minutes.");
        if (price is < 0 or > 1_000_000) return Result.Fail("service.price_invalid", "The price must be from R0 to R10000.");
        return Result.Ok();
    }
}

/// <summary>Which staff can do which service. A service with nobody assigned can be done by anyone.</summary>
public sealed class StaffService : Entity
{
    private StaffService() { }

    public StaffService(Guid shopId, Guid staffId, Guid serviceId, DateTimeOffset now)
    {
        ShopId = shopId;
        StaffId = staffId;
        ServiceId = serviceId;
        CreatedAt = now;
    }

    public Guid ShopId { get; private set; }
    public Guid StaffId { get; private set; }
    public Guid ServiceId { get; private set; }
}

public enum AppointmentStatus { Booked, Confirmed, Completed, Cancelled, NoShow }

public sealed record AppointmentInput(
    Guid Id, Guid ShopId, Guid StaffId, Guid BookedBy, Guid? DeviceId, Guid ServiceId, string ServiceName, int DurationMinutes, long PriceCents,
    Guid? CustomerId, string? CustomerName, string? CustomerPhone, DateTimeOffset StartsAt, string? Notes);

/// <summary>
/// B5-2: one person's time, booked for one customer. A cancelled or no-show appointment frees the time. Two live appointments for the same staff member
/// never overlap; when an offline till books one that does, it is saved and flagged so staff can sort it out, rather than lost.
/// </summary>
public sealed class Appointment : VersionedEntity
{
    public static readonly TimeSpan MaxAdvance = TimeSpan.FromDays(366);
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromHours(24);

    private Appointment() { }

    public Guid ShopId { get; private set; }
    public Guid StaffId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string ServiceName { get; private set; } = default!;

    /// <summary>The price when it was booked, so a later price change does not rewrite history or commission.</summary>
    public long PriceCents { get; private set; }

    public Guid? CustomerId { get; private set; }
    public string CustomerName { get; private set; } = default!;
    public string? CustomerPhone { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public bool HasConflict { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset? ReminderSentAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public bool BlocksTime => Status is AppointmentStatus.Booked or AppointmentStatus.Confirmed or AppointmentStatus.Completed;

    public static Result<Appointment> Book(AppointmentInput i, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(i.CustomerName) && i.CustomerId is null) return Result<Appointment>.Fail("appointment.customer_required", "Say who the appointment is for.");
        if (i.CustomerName is { Length: > 200 } || i.CustomerPhone is { Length: > 20 } || i.Notes is { Length: > 500 }) return Result<Appointment>.Fail("appointment.text_too_long", "A name, phone number or note is too long.");
        if (i.StartsAt.Minute % 5 != 0 || i.StartsAt.Second != 0) return Result<Appointment>.Fail("appointment.time_invalid", "Appointments start on the hour or at 5-minute steps.");
        if (i.StartsAt > now + MaxAdvance) return Result<Appointment>.Fail("appointment.too_far_ahead", "Appointments can be booked up to a year ahead.");

        var appointment = new Appointment
        {
            ShopId = i.ShopId, StaffId = i.StaffId, ServiceId = i.ServiceId, ServiceName = i.ServiceName, PriceCents = i.PriceCents, CustomerId = i.CustomerId,
            CustomerName = string.IsNullOrWhiteSpace(i.CustomerName) ? "Customer" : i.CustomerName.Trim(), CustomerPhone = string.IsNullOrWhiteSpace(i.CustomerPhone) ? null : i.CustomerPhone.Trim(),
            StartsAt = i.StartsAt, EndsAt = i.StartsAt.AddMinutes(i.DurationMinutes), Status = AppointmentStatus.Booked, Notes = string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim(),
        };
        appointment.StampFromDevice(i.Id, now, i.BookedBy, i.DeviceId);
        return Result<Appointment>.Ok(appointment);
    }

    public bool Overlaps(Appointment other) => Id != other.Id && StaffId == other.StaffId && BlocksTime && other.BlocksTime && StartsAt < other.EndsAt && other.StartsAt < EndsAt;

    public void FlagConflict() { HasConflict = true; }
    public void ClearConflict() { HasConflict = false; }

    public Result Confirm()
    {
        if (Status != AppointmentStatus.Booked) return Result.Fail("appointment.not_booked", "Only a booked appointment can be confirmed.");
        Status = AppointmentStatus.Confirmed;
        Touch();
        return Result.Ok();
    }

    /// <summary>The service was done. Only then does it count towards commission.</summary>
    public Result Complete(DateTimeOffset now)
    {
        if (Status is not (AppointmentStatus.Booked or AppointmentStatus.Confirmed)) return Result.Fail("appointment.not_open", "This appointment is already finished or cancelled.");
        Status = AppointmentStatus.Completed;
        CompletedAt = now;
        Touch();
        return Result.Ok();
    }

    public Result Cancel()
    {
        if (Status is not (AppointmentStatus.Booked or AppointmentStatus.Confirmed)) return Result.Fail("appointment.not_open", "This appointment is already finished or cancelled.");
        Status = AppointmentStatus.Cancelled;
        HasConflict = false;
        Touch();
        return Result.Ok();
    }

    public Result MarkNoShow(DateTimeOffset now)
    {
        if (Status is not (AppointmentStatus.Booked or AppointmentStatus.Confirmed)) return Result.Fail("appointment.not_open", "This appointment is already finished or cancelled.");
        if (now < StartsAt) return Result.Fail("appointment.not_started", "The appointment has not started yet.");
        Status = AppointmentStatus.NoShow;
        HasConflict = false;
        Touch();
        return Result.Ok();
    }

    /// <summary>Moves it to another time, and another staff member if wanted. Reminders start again for the new time.</summary>
    public Result Reschedule(DateTimeOffset newStart, Guid? newStaffId, int durationMinutes, DateTimeOffset now)
    {
        if (Status is not (AppointmentStatus.Booked or AppointmentStatus.Confirmed)) return Result.Fail("appointment.not_open", "This appointment is already finished or cancelled.");
        if (newStart.Minute % 5 != 0 || newStart.Second != 0) return Result.Fail("appointment.time_invalid", "Appointments start on the hour or at 5-minute steps.");
        if (newStart < now - MaxClockSkew || newStart > now + MaxAdvance) return Result.Fail("appointment.time_invalid", "Choose a time from now up to a year ahead.");
        StartsAt = newStart;
        EndsAt = newStart.AddMinutes(durationMinutes);
        if (newStaffId is { } staff) StaffId = staff;
        ReminderSentAt = null;
        HasConflict = false;
        Touch();
        return Result.Ok();
    }

    public void NoteReminderSent(DateTimeOffset at) => ReminderSentAt = at;
}

/// <summary>
/// B5-3: what a staff member earns on a service, as a percentage of its price. The most specific rule wins: this staff member for this service, then
/// this service for anyone, then this staff member for anything, then the shop-wide default. No rule means no commission.
/// </summary>
public sealed class CommissionRule : VersionedEntity
{
    private CommissionRule() { }

    public Guid ShopId { get; private set; }
    public Guid? StaffId { get; private set; }
    public Guid? ServiceId { get; private set; }
    public int Percent { get; private set; }

    public static Result<CommissionRule> Create(Guid shopId, Guid? staffId, Guid? serviceId, int percent, DateTimeOffset now) =>
        percent is < 0 or > 100
            ? Result<CommissionRule>.Fail("commission.percent_invalid", "A commission is from 0 to 100 percent.")
            : Result<CommissionRule>.Ok(new CommissionRule { ShopId = shopId, StaffId = staffId, ServiceId = serviceId, Percent = percent, CreatedAt = now });

    public void SetPercent(int percent)
    {
        Percent = percent;
        Touch();
    }
}

public static class Commission
{
    public static int? PercentFor(IReadOnlyCollection<CommissionRule> rules, Guid staffId, Guid serviceId) =>
        (rules.FirstOrDefault(r => r.StaffId == staffId && r.ServiceId == serviceId)
         ?? rules.FirstOrDefault(r => r.StaffId is null && r.ServiceId == serviceId)
         ?? rules.FirstOrDefault(r => r.StaffId == staffId && r.ServiceId is null)
         ?? rules.FirstOrDefault(r => r.StaffId is null && r.ServiceId is null))?.Percent;

    /// <summary>Half up to the cent.</summary>
    public static long Of(long priceCents, int percent) => (priceCents * percent + 50) / 100;
}
