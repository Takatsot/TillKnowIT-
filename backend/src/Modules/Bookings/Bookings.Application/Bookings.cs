using Bookings.Contracts;
using Bookings.Domain;
using Customers.Contracts;
using Identity.Contracts;
using Messaging.Contracts;
using Platform.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.Reporting;
using TillKnowIT.SharedKernel;

namespace Bookings.Application;

public interface IBookingStore
{
    void Add(ServiceItem service);
    Task<ServiceItem?> GetService(Guid shopId, Guid serviceId, CancellationToken ct);
    Task<List<ServiceItem>> Services(Guid shopId, CancellationToken ct);
    Task<List<StaffService>> StaffServices(Guid shopId, CancellationToken ct);
    Task ReplaceStaff(Guid shopId, Guid serviceId, IReadOnlyList<Guid> staffIds, DateTimeOffset now, CancellationToken ct);

    void Add(Appointment appointment);
    Task<Appointment?> GetAppointment(Guid shopId, Guid appointmentId, CancellationToken ct);
    Task<List<Appointment>> Between(Guid shopId, DateTimeOffset from, DateTimeOffset to, Guid? staffId, CancellationToken ct);
    Task<List<Appointment>> StartingBetween(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    void Add(CommissionRule rule);
    Task<CommissionRule?> GetRule(Guid shopId, Guid? staffId, Guid? serviceId, CancellationToken ct);
    Task<List<CommissionRule>> Rules(Guid shopId, CancellationToken ct);
    Task<List<Appointment>> CompletedBetween(Guid shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task Save(CancellationToken ct);
}

internal static class BookingAccess
{
    public static async Task<Result> Till(ITillAccessChecker tills, IShopFeatures features, Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct)
    {
        var allowed = await tills.Check(shopId, deviceId, staffId, permission, ct);
        if (!allowed.IsSuccess) return allowed;
        return await features.IsOn(shopId, "appointments", ct) ? Result.Ok() : Result.Fail("feature.not_enabled", "'appointments' is not switched on for this shop.");
    }

    public static async Task<Result> Owner(IOrganisationAccess access, IShopFeatures features, Guid personId, Guid shopId, string permission, string feature, CancellationToken ct)
    {
        if (!await access.Has(personId, shopId, permission, ct)) return Result.Fail("permission.denied", "You do not have access to this shop.");
        return await features.IsOn(shopId, feature, ct) ? Result.Ok() : Result.Fail("feature.not_enabled", $"'{feature}' is not switched on for this shop.");
    }

    public static AppointmentResponse ToResponse(Appointment a) => new(
        a.Id, a.ServiceId, a.ServiceName, a.StaffId, a.CustomerId, a.CustomerName, a.CustomerPhone, a.StartsAt, a.EndsAt, a.Status.ToString().ToLowerInvariant(), a.HasConflict, a.PriceCents, a.Notes);
}

// ---------------------------------------------------------------- B5-1: services

public sealed record SaveService(Guid ShopId, Guid? ServiceId, SaveServiceRequest Request) : ICommand<ServiceResponse>;
public sealed record ListServices(Guid ShopId, Guid DeviceId, Guid StaffId) : IQuery<List<ServiceResponse>>;

internal sealed class ServiceHandlers(IBookingStore store, IOrganisationAccess access, ITillAccessChecker tills, IShopFeatures features, TimeProvider clock)
    : ICommandHandler<SaveService, ServiceResponse>, IQueryHandler<ListServices, List<ServiceResponse>>
{
    public async Task<Result<ServiceResponse>> Handle(SaveService c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await BookingAccess.Owner(access, features, r.CallerPersonId, c.ShopId, "shop.settings", "appointments", ct);
        if (!allowed.IsSuccess) return Result<ServiceResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var now = clock.GetUtcNow();
        ServiceItem service;
        if (c.ServiceId is { } id)
        {
            var existing = await store.GetService(c.ShopId, id, ct);
            if (existing is null) return Result<ServiceResponse>.Fail("service.not_found", "Service not found.");
            var changed = existing.Change(r.Name, r.DurationMinutes, r.PriceCents, r.IsActive);
            if (!changed.IsSuccess) return Result<ServiceResponse>.Fail(changed.Error!.Code, changed.Error.Message);
            service = existing;
        }
        else
        {
            var created = ServiceItem.Create(c.ShopId, r.Name, r.DurationMinutes, r.PriceCents, now);
            if (!created.IsSuccess) return Result<ServiceResponse>.Fail(created.Error!.Code, created.Error.Message);
            service = created.Value!;
            store.Add(service);
        }

        var staff = (r.StaffIds ?? []).Distinct().ToList();
        if (r.StaffIds is not null) await store.ReplaceStaff(c.ShopId, service.Id, staff, now, ct);
        await store.Save(ct);
        return Result<ServiceResponse>.Ok(new ServiceResponse(service.Id, service.Name, service.DurationMinutes, service.PriceCents, service.IsActive,
            (await store.StaffServices(c.ShopId, ct)).Where(s => s.ServiceId == service.Id).Select(s => s.StaffId).ToList()));
    }

    public async Task<Result<List<ServiceResponse>>> Handle(ListServices q, CancellationToken ct)
    {
        var allowed = await BookingAccess.Till(tills, features, q.ShopId, q.DeviceId, q.StaffId, "sale.create", ct);
        if (!allowed.IsSuccess) return Result<List<ServiceResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var staff = (await store.StaffServices(q.ShopId, ct)).GroupBy(s => s.ServiceId).ToDictionary(g => g.Key, g => g.Select(s => s.StaffId).ToList());
        return Result<List<ServiceResponse>>.Ok((await store.Services(q.ShopId, ct)).OrderBy(s => s.Name)
            .Select(s => new ServiceResponse(s.Id, s.Name, s.DurationMinutes, s.PriceCents, s.IsActive, staff.GetValueOrDefault(s.Id) ?? [])).ToList());
    }
}

// ---------------------------------------------------------------- B5-2: the calendar

public sealed record BookAppointment(Guid ShopId, BookAppointmentRequest Request) : ICommand<AppointmentResponse>;
public sealed record ListAppointments(Guid ShopId, Guid DeviceId, Guid StaffId, DateTimeOffset From, DateTimeOffset To, Guid? ProviderStaffId) : IQuery<List<AppointmentResponse>>;
public sealed record RescheduleAppointment(Guid ShopId, Guid AppointmentId, RescheduleRequest Request) : ICommand<AppointmentResponse>;
public sealed record ChangeAppointment(Guid ShopId, Guid AppointmentId, AppointmentActionRequest Request) : ICommand<AppointmentResponse>;

internal sealed class AppointmentHandlers(IBookingStore store, ITillAccessChecker tills, IShopFeatures features, TimeProvider clock)
    : ICommandHandler<BookAppointment, AppointmentResponse>, IQueryHandler<ListAppointments, List<AppointmentResponse>>,
      ICommandHandler<RescheduleAppointment, AppointmentResponse>, ICommandHandler<ChangeAppointment, AppointmentResponse>
{
    public const int MaxRangeDays = 62;

    private Task<Result> Allowed(Guid shop, Guid device, Guid staff, CancellationToken ct) => BookingAccess.Till(tills, features, shop, device, staff, "sale.create", ct);

    /// <summary>
    /// The same appointment id twice is one appointment. A clash with the staff member's other appointments is refused online and, for a till that booked
    /// while offline (AllowConflict), saved and flagged on both so staff see it.
    /// </summary>
    public async Task<Result<AppointmentResponse>> Handle(BookAppointment c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<AppointmentResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (await store.GetAppointment(c.ShopId, r.Id, ct) is { } same) return Result<AppointmentResponse>.Ok(BookingAccess.ToResponse(same));

        var service = await store.GetService(c.ShopId, r.ServiceId, ct);
        if (service is null || !service.IsActive) return Result<AppointmentResponse>.Fail("service.not_found", "That service is not offered.");
        var assigned = (await store.StaffServices(c.ShopId, ct)).Where(s => s.ServiceId == service.Id).Select(s => s.StaffId).ToList();
        if (assigned.Count > 0 && !assigned.Contains(r.ProviderStaffId)) return Result<AppointmentResponse>.Fail("appointment.staff_cannot_do_service", "That person does not do this service.");

        var now = clock.GetUtcNow();
        if (!r.AllowConflict && r.StartsAt < now - Appointment.MaxClockSkew) return Result<AppointmentResponse>.Fail("appointment.in_the_past", "That time has already passed.");

        var booked = Appointment.Book(new AppointmentInput(r.Id, c.ShopId, r.ProviderStaffId, r.StaffId, r.DeviceId, service.Id, service.Name, service.DurationMinutes, service.PriceCents,
            r.CustomerId, r.CustomerName, r.CustomerPhone, r.StartsAt, r.Notes), now);
        if (!booked.IsSuccess) return Result<AppointmentResponse>.Fail(booked.Error!.Code, booked.Error.Message);
        var appointment = booked.Value!;

        var clashes = (await store.Between(c.ShopId, appointment.StartsAt.AddDays(-1), appointment.EndsAt.AddDays(1), appointment.StaffId, ct)).Where(a => appointment.Overlaps(a)).ToList();
        if (clashes.Count > 0)
        {
            if (!r.AllowConflict) return Result<AppointmentResponse>.Fail("appointment.conflict", $"{clashes[0].CustomerName} is booked at {clashes[0].StartsAt:HH:mm}. Choose another time or another person.");
            appointment.FlagConflict();
            foreach (var other in clashes) other.FlagConflict();
        }

        store.Add(appointment);
        await store.Save(ct);
        return Result<AppointmentResponse>.Ok(BookingAccess.ToResponse(appointment));
    }

    public async Task<Result<List<AppointmentResponse>>> Handle(ListAppointments q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<List<AppointmentResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (q.To <= q.From || (q.To - q.From).TotalDays > MaxRangeDays) return Result<List<AppointmentResponse>>.Fail("appointment.range_invalid", $"Ask for up to {MaxRangeDays} days at a time.");
        return Result<List<AppointmentResponse>>.Ok((await store.Between(q.ShopId, q.From, q.To, q.ProviderStaffId, ct)).OrderBy(a => a.StartsAt).Select(BookingAccess.ToResponse).ToList());
    }

    public async Task<Result<AppointmentResponse>> Handle(RescheduleAppointment c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<AppointmentResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var appointment = await store.GetAppointment(c.ShopId, c.AppointmentId, ct);
        if (appointment is null) return Result<AppointmentResponse>.Fail("appointment.not_found", "Appointment not found.");
        var service = await store.GetService(c.ShopId, appointment.ServiceId, ct);
        var minutes = service?.DurationMinutes ?? (int)(appointment.EndsAt - appointment.StartsAt).TotalMinutes;

        // Look for a clash before changing anything, so a refused move leaves the appointment exactly as it was.
        var staff = r.ProviderStaffId ?? appointment.StaffId;
        var newEnd = r.StartsAt.AddMinutes(minutes);
        var clashes = (await store.Between(c.ShopId, r.StartsAt.AddDays(-1), newEnd.AddDays(1), staff, ct))
            .Where(a => a.Id != appointment.Id && a.BlocksTime && r.StartsAt < a.EndsAt && a.StartsAt < newEnd).ToList();
        if (clashes.Count > 0 && !r.AllowConflict) return Result<AppointmentResponse>.Fail("appointment.conflict", $"{clashes[0].CustomerName} is booked at {clashes[0].StartsAt:HH:mm}.");

        var moved = appointment.Reschedule(r.StartsAt, r.ProviderStaffId, minutes, clock.GetUtcNow());
        if (!moved.IsSuccess) return Result<AppointmentResponse>.Fail(moved.Error!.Code, moved.Error.Message);
        if (clashes.Count > 0) { appointment.FlagConflict(); foreach (var other in clashes) other.FlagConflict(); }

        await store.Save(ct);
        return Result<AppointmentResponse>.Ok(BookingAccess.ToResponse(appointment));
    }

    public async Task<Result<AppointmentResponse>> Handle(ChangeAppointment c, CancellationToken ct)
    {
        var allowed = await Allowed(c.ShopId, c.Request.DeviceId, c.Request.StaffId, ct);
        if (!allowed.IsSuccess) return Result<AppointmentResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var appointment = await store.GetAppointment(c.ShopId, c.AppointmentId, ct);
        if (appointment is null) return Result<AppointmentResponse>.Fail("appointment.not_found", "Appointment not found.");

        var now = clock.GetUtcNow();
        var done = c.Request.Action?.Trim().ToLowerInvariant() switch
        {
            "confirm" => appointment.Confirm(),
            "complete" => appointment.Complete(now),
            "cancel" => appointment.Cancel(),
            "no_show" => appointment.MarkNoShow(now),
            _ => Result.Fail("appointment.action_invalid", "Action is confirm, complete, cancel or no_show."),
        };
        if (!done.IsSuccess) return Result<AppointmentResponse>.Fail(done.Error!.Code, done.Error.Message);

        // Freeing the time may settle a flagged clash: re-check the others that overlapped it.
        if (!appointment.BlocksTime) await ClearSettledConflicts(c.ShopId, appointment, ct);
        await store.Save(ct);
        return Result<AppointmentResponse>.Ok(BookingAccess.ToResponse(appointment));
    }

    private async Task ClearSettledConflicts(Guid shopId, Appointment freed, CancellationToken ct)
    {
        var around = await store.Between(shopId, freed.StartsAt.AddDays(-1), freed.EndsAt.AddDays(1), freed.StaffId, ct);
        foreach (var a in around.Where(a => a.HasConflict && a.Id != freed.Id))
            if (!around.Any(other => other.Id != freed.Id && a.Overlaps(other))) a.ClearConflict();
    }
}

// ---------------------------------------------------------------- B5-2: reminders

public sealed record SendAppointmentReminders : ICommand<int>;

/// <summary>
/// Reminds each customer about tomorrow's appointment, once. Only customers who agreed to be messaged (through the customer list) are messaged;
/// a customer who is only a name and a number is never texted. WhatsApp first: if the shop's slip and reminder bundle is used up, the reminder is skipped
/// and tried again on the next round.
/// </summary>
internal sealed class ReminderHandler(IBookingStore store, ICustomerDirectory customers, IMessageQueue queue, TimeProvider clock) : ICommandHandler<SendAppointmentReminders, int>
{
    public static readonly TimeSpan LeadTime = TimeSpan.FromHours(24);

    public async Task<Result<int>> Handle(SendAppointmentReminders c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var sent = 0;
        foreach (var a in await store.StartingBetween(now, now + LeadTime, ct))
        {
            if (a.ReminderSentAt is not null || a.Status is not (AppointmentStatus.Booked or AppointmentStatus.Confirmed) || a.CustomerId is not { } customerId) continue;
            var customer = await customers.Get(a.ShopId, customerId, ct);
            if (customer is not { MessageConsent: true }) continue;

            var local = a.StartsAt.ToOffset(BusinessDay.Offset);
            var result = await queue.Queue(new QueueMessageRequest("appointment.reminder", "en", "whatsapp", customer.Phone,
                new() { ["name"] = customer.Name, ["service"] = a.ServiceName, ["date"] = local.ToString("dddd d MMMM"), ["time"] = local.ToString("HH:mm") }, a.ShopId, $"appointment:{a.Id:N}:reminder"), ct);
            if (!result.IsSuccess) continue;   // for instance a used-up bundle: the next round tries again

            a.NoteReminderSent(now);
            sent++;
        }
        if (sent > 0) await store.Save(ct);
        return Result<int>.Ok(sent);
    }
}

// ---------------------------------------------------------------- B5-3: commission

public sealed record SaveCommissionRule(Guid ShopId, SaveCommissionRuleRequest Request) : ICommand<CommissionRuleResponse>;
public sealed record ListCommissionRules(Guid ShopId, Guid CallerPersonId) : IQuery<List<CommissionRuleResponse>>;
public sealed record GetCommissionReport(Guid ShopId, Guid CallerPersonId, DateOnly? From, DateOnly? To, string? Format) : IQuery<ReportFile>;

public static class CommissionReportBuilder
{
    public static List<CommissionLine> Lines(IEnumerable<Appointment> completed, IReadOnlyCollection<CommissionRule> rules) =>
        completed.GroupBy(a => a.StaffId).Select(g => new CommissionLine(g.Key, g.Count(), g.Sum(a => a.PriceCents),
            g.Sum(a => Commission.PercentFor(rules, a.StaffId, a.ServiceId) is { } p ? Commission.Of(a.PriceCents, p) : 0))).OrderBy(l => l.StaffId).ToList();

    public static ReportTable Table(IReadOnlyList<CommissionLine> lines, IReadOnlyDictionary<Guid, string> staffNames, string period) => new("Staff commission",
        [new("Staff"), new("Services", ColumnKind.Integer), new("Revenue", ColumnKind.Money), new("Commission", ColumnKind.Money)],
        lines.Select(l => new object?[] { staffNames.GetValueOrDefault(l.StaffId) ?? l.StaffId.ToString("N")[..8], l.Services, l.RevenueCents, l.CommissionCents }).ToList())
    {
        Notes = [$"Period: {period}", $"Total commission: R {CellText.Rand(lines.Sum(l => l.CommissionCents))}"],
    };
}

internal sealed class CommissionHandlers(IBookingStore store, IOrganisationAccess access, IShopFeatures features, TimeProvider clock)
    : ICommandHandler<SaveCommissionRule, CommissionRuleResponse>, IQueryHandler<ListCommissionRules, List<CommissionRuleResponse>>, IQueryHandler<GetCommissionReport, ReportFile>
{
    public async Task<Result<CommissionRuleResponse>> Handle(SaveCommissionRule c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await BookingAccess.Owner(access, features, r.CallerPersonId, c.ShopId, "shop.settings", "commission", ct);
        if (!allowed.IsSuccess) return Result<CommissionRuleResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var existing = await store.GetRule(c.ShopId, r.StaffId, r.ServiceId, ct);
        if (existing is not null)
        {
            if (r.Percent is < 0 or > 100) return Result<CommissionRuleResponse>.Fail("commission.percent_invalid", "A commission is from 0 to 100 percent.");
            existing.SetPercent(r.Percent);
            await store.Save(ct);
            return Result<CommissionRuleResponse>.Ok(new CommissionRuleResponse(existing.Id, existing.StaffId, existing.ServiceId, existing.Percent));
        }

        var created = CommissionRule.Create(c.ShopId, r.StaffId, r.ServiceId, r.Percent, clock.GetUtcNow());
        if (!created.IsSuccess) return Result<CommissionRuleResponse>.Fail(created.Error!.Code, created.Error.Message);
        store.Add(created.Value!);
        await store.Save(ct);
        return Result<CommissionRuleResponse>.Ok(new CommissionRuleResponse(created.Value!.Id, created.Value.StaffId, created.Value.ServiceId, created.Value.Percent));
    }

    public async Task<Result<List<CommissionRuleResponse>>> Handle(ListCommissionRules q, CancellationToken ct)
    {
        var allowed = await BookingAccess.Owner(access, features, q.CallerPersonId, q.ShopId, "report.view", "commission", ct);
        if (!allowed.IsSuccess) return Result<List<CommissionRuleResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<List<CommissionRuleResponse>>.Ok((await store.Rules(q.ShopId, ct)).Select(r => new CommissionRuleResponse(r.Id, r.StaffId, r.ServiceId, r.Percent)).ToList());
    }

    /// <summary>What each staff member earned over the period, from appointments marked completed, as csv, xlsx or pdf. Business days, both ends included.</summary>
    public async Task<Result<ReportFile>> Handle(GetCommissionReport q, CancellationToken ct)
    {
        var allowed = await BookingAccess.Owner(access, features, q.CallerPersonId, q.ShopId, "report.view", "commission", ct);
        if (!allowed.IsSuccess) return Result<ReportFile>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (!ReportFormats.TryParse(q.Format, out var format)) return Result<ReportFile>.Fail("report.format_invalid", "Format must be csv, xlsx or pdf.");

        var to = q.To ?? BusinessDay.Of(clock.GetUtcNow());
        var from = q.From ?? to.AddDays(-29);
        if (from > to) return Result<ReportFile>.Fail("report.range_invalid", "The start date is after the end date.");
        if (to.DayNumber - from.DayNumber >= 366) return Result<ReportFile>.Fail("report.range_too_large", "A report covers at most a year.");

        var completed = await store.CompletedBetween(q.ShopId, BusinessDay.Start(from), BusinessDay.Start(to.AddDays(1)), ct);
        var lines = CommissionReportBuilder.Lines(completed, await store.Rules(q.ShopId, ct));
        return Result<ReportFile>.Ok(ReportExporter.Export(CommissionReportBuilder.Table(lines, new Dictionary<Guid, string>(), $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}"), format, $"commission-{from:yyyyMMdd}-{to:yyyyMMdd}"));
    }
}
