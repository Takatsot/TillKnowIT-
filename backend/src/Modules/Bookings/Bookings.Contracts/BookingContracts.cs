namespace Bookings.Contracts;

public sealed record SaveServiceRequest(Guid CallerPersonId, string Name, int DurationMinutes, long PriceCents, bool IsActive, List<Guid>? StaffIds);

public sealed record ServiceResponse(Guid Id, string Name, int DurationMinutes, long PriceCents, bool IsActive, List<Guid> StaffIds);

/// <summary>
/// Id is made on the till. CustomerId (from the customer list) is how reminders know the customer agreed to be messaged; a customer who is only a
/// name and number gets no reminders. AllowConflict is for appointments booked offline and synced later: they are saved and flagged, not refused.
/// </summary>
public sealed record BookAppointmentRequest(
    Guid DeviceId, Guid StaffId, Guid Id, Guid ServiceId, Guid ProviderStaffId, Guid? CustomerId, string? CustomerName, string? CustomerPhone,
    DateTimeOffset StartsAt, string? Notes, bool AllowConflict = false);

public sealed record AppointmentResponse(
    Guid Id, Guid ServiceId, string ServiceName, Guid StaffId, Guid? CustomerId, string CustomerName, string? CustomerPhone, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string Status, bool HasConflict, long PriceCents, string? Notes);

public sealed record RescheduleRequest(Guid DeviceId, Guid StaffId, DateTimeOffset StartsAt, Guid? ProviderStaffId, bool AllowConflict = false);

/// <summary>Action is confirm, complete, cancel or no_show.</summary>
public sealed record AppointmentActionRequest(Guid DeviceId, Guid StaffId, string Action);

public sealed record SaveCommissionRuleRequest(Guid CallerPersonId, Guid? StaffId, Guid? ServiceId, int Percent);

public sealed record CommissionRuleResponse(Guid Id, Guid? StaffId, Guid? ServiceId, int Percent);

public sealed record CommissionLine(Guid StaffId, int Services, long RevenueCents, long CommissionCents);
