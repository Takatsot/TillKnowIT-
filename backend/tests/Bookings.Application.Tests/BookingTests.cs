using Bookings.Application;
using Bookings.Contracts;
using Bookings.Domain;
using Customers.Contracts;
using Identity.Contracts;
using Messaging.Contracts;
using Microsoft.Extensions.Time.Testing;
using Platform.Contracts;
using TillKnowIT.Reporting;
using TillKnowIT.SharedKernel;

namespace Bookings.Application.Tests;

public class BookingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Anna = Guid.NewGuid();
    private static readonly Guid Ben = Guid.NewGuid();
    private static readonly Guid Cut = Guid.NewGuid();

    private static Appointment Book(Guid? staff = null, int hour = 10, int minute = 0, int duration = 45, string name = "Thandi") =>
        Appointment.Book(new(Guid.NewGuid(), Shop, staff ?? Anna, Guid.NewGuid(), Guid.NewGuid(), Cut, "Haircut", duration, 15_000, null, name, "0821234567",
            new DateTimeOffset(2026, 10, 27, hour, minute, 0, TimeSpan.Zero), null), Now).Value!;

    [Fact]
    public void A_service_has_a_name_a_duration_in_five_minute_steps_and_a_price()
    {
        Assert.Equal("service.name_invalid", ServiceItem.Create(Shop, " ", 30, 100, Now).Error!.Code);
        Assert.Equal("service.duration_invalid", ServiceItem.Create(Shop, "Cut", 4, 100, Now).Error!.Code);
        Assert.Equal("service.duration_invalid", ServiceItem.Create(Shop, "Cut", 33, 100, Now).Error!.Code);
        Assert.Equal("service.duration_invalid", ServiceItem.Create(Shop, "Cut", 481, 100, Now).Error!.Code);
        Assert.Equal("service.price_invalid", ServiceItem.Create(Shop, "Cut", 30, -1, Now).Error!.Code);
        Assert.True(ServiceItem.Create(Shop, "Cut", 45, 15_000, Now).IsSuccess);
    }

    [Fact]
    public void An_appointment_runs_from_its_start_for_the_services_duration_and_keeps_the_price_it_was_booked_at()
    {
        var a = Book(duration: 45);

        Assert.Equal((a.StartsAt.AddMinutes(45), 15_000L, AppointmentStatus.Booked), (a.EndsAt, a.PriceCents, a.Status));
    }

    [Fact]
    public void An_appointment_needs_someone_to_be_for_and_a_time_on_the_hour_or_in_five_minute_steps()
    {
        AppointmentInput Input(string? name, Guid? customer, DateTimeOffset start) => new(Guid.NewGuid(), Shop, Anna, Guid.NewGuid(), null, Cut, "Haircut", 30, 100, customer, name, null, start, null);

        Assert.Equal("appointment.customer_required", Appointment.Book(Input(" ", null, Now.AddDays(1)), Now).Error!.Code);
        Assert.True(Appointment.Book(Input(null, Guid.NewGuid(), Now.AddDays(1)), Now).IsSuccess);
        Assert.Equal("appointment.time_invalid", Appointment.Book(Input("A", null, new DateTimeOffset(2026, 10, 27, 10, 7, 0, TimeSpan.Zero)), Now).Error!.Code);
        Assert.Equal("appointment.too_far_ahead", Appointment.Book(Input("A", null, Now.AddDays(400)), Now).Error!.Code);
    }

    [Fact]
    public void Overlap_is_only_for_the_same_staff_member_and_back_to_back_is_fine()
    {
        var ten = Book(Anna, 10, 0, 45);

        Assert.True(ten.Overlaps(Book(Anna, 10, 30, 45)));
        Assert.True(ten.Overlaps(Book(Anna, 9, 30, 45)));
        Assert.True(ten.Overlaps(Book(Anna, 10, 0, 45)));
        Assert.False(ten.Overlaps(Book(Anna, 10, 45, 30)));    // starts exactly as the first ends
        Assert.False(ten.Overlaps(Book(Anna, 9, 15, 45)));     // ends exactly as the first starts
        Assert.False(ten.Overlaps(Book(Ben, 10, 0, 45)));
        Assert.False(ten.Overlaps(ten));
    }

    [Fact]
    public void A_cancelled_or_no_show_appointment_frees_the_time()
    {
        var ten = Book(Anna, 10, 0, 45);
        var other = Book(Anna, 10, 15, 45);
        other.Cancel();

        Assert.False(ten.Overlaps(other));
        Assert.False(other.BlocksTime);

        var late = Book(Anna, 12, 0, 30);
        Assert.Equal("appointment.not_started", late.MarkNoShow(Now).Error!.Code);
        Assert.True(late.MarkNoShow(new DateTimeOffset(2026, 10, 27, 12, 30, 0, TimeSpan.Zero)).IsSuccess);
        Assert.False(late.BlocksTime);
    }

    [Fact]
    public void The_status_only_moves_forward_and_a_finished_appointment_is_closed()
    {
        var a = Book();

        Assert.True(a.Confirm().IsSuccess);
        Assert.Equal("appointment.not_booked", a.Confirm().Error!.Code);
        Assert.True(a.Complete(Now.AddDays(1)).IsSuccess);
        Assert.Equal(Now.AddDays(1), a.CompletedAt);
        Assert.Equal("appointment.not_open", a.Cancel().Error!.Code);
        Assert.Equal("appointment.not_open", a.Complete(Now).Error!.Code);
        Assert.True(a.BlocksTime);   // a done appointment still occupies its slot in the calendar
    }

    [Fact]
    public void Rescheduling_moves_it_and_clears_the_reminder_and_the_conflict_flag()
    {
        var a = Book();
        a.NoteReminderSent(Now);
        a.FlagConflict();

        var moved = a.Reschedule(new DateTimeOffset(2026, 10, 28, 14, 0, 0, TimeSpan.Zero), Ben, 45, Now);

        Assert.True(moved.IsSuccess);
        Assert.Equal((new DateTimeOffset(2026, 10, 28, 14, 45, 0, TimeSpan.Zero), Ben, null, false), (a.EndsAt, a.StaffId, a.ReminderSentAt, a.HasConflict));
        Assert.Equal("appointment.time_invalid", a.Reschedule(new DateTimeOffset(2026, 10, 28, 14, 3, 0, TimeSpan.Zero), null, 45, Now).Error!.Code);
        Assert.Equal("appointment.time_invalid", a.Reschedule(Now.AddDays(-3), null, 45, Now).Error!.Code);
    }

    [Fact]
    public void The_most_specific_commission_rule_wins_and_no_rule_means_no_commission()
    {
        CommissionRule Rule(Guid? staff, Guid? service, int pct) => CommissionRule.Create(Shop, staff, service, pct, Now).Value!;
        var color = Guid.NewGuid();
        var rules = new[] { Rule(null, null, 10), Rule(Anna, null, 20), Rule(null, Cut, 30), Rule(Anna, Cut, 40) };

        Assert.Equal(40, Commission.PercentFor(rules, Anna, Cut));      // this staff member, this service
        Assert.Equal(30, Commission.PercentFor(rules, Ben, Cut));       // this service for anyone
        Assert.Equal(20, Commission.PercentFor(rules, Anna, color));    // this staff member for anything
        Assert.Equal(10, Commission.PercentFor(rules, Ben, color));     // the shop default
        Assert.Null(Commission.PercentFor([], Anna, Cut));
        Assert.Equal("commission.percent_invalid", CommissionRule.Create(Shop, null, null, 101, Now).Error!.Code);
        Assert.Equal(1_500, Commission.Of(15_000, 10));
        Assert.Equal(2, Commission.Of(15, 10));      // 1.5 rounds up
    }
}

public class BookingFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);   // Monday 10:00 in South Africa

    private sealed class Store : IBookingStore
    {
        public List<ServiceItem> ServiceRows { get; } = [];
        public List<StaffService> Assignments { get; } = [];
        public List<Appointment> Appointments { get; } = [];
        public List<CommissionRule> RuleRows { get; } = [];
        public void Add(ServiceItem s) => ServiceRows.Add(s);
        public Task<ServiceItem?> GetService(Guid shopId, Guid id, CancellationToken ct) => Task.FromResult(ServiceRows.FirstOrDefault(s => s.ShopId == shopId && s.Id == id));
        public Task<List<ServiceItem>> Services(Guid shopId, CancellationToken ct) => Task.FromResult(ServiceRows.Where(s => s.ShopId == shopId).ToList());
        public Task<List<StaffService>> StaffServices(Guid shopId, CancellationToken ct) => Task.FromResult(Assignments.Where(a => a.ShopId == shopId).ToList());
        public Task ReplaceStaff(Guid shopId, Guid serviceId, IReadOnlyList<Guid> staff, DateTimeOffset now, CancellationToken ct)
        {
            Assignments.RemoveAll(a => a.ShopId == shopId && a.ServiceId == serviceId);
            Assignments.AddRange(staff.Select(s => new StaffService(shopId, s, serviceId, now)));
            return Task.CompletedTask;
        }
        public void Add(Appointment a) => Appointments.Add(a);
        public Task<Appointment?> GetAppointment(Guid shopId, Guid id, CancellationToken ct) => Task.FromResult(Appointments.FirstOrDefault(a => a.ShopId == shopId && a.Id == id));
        public Task<List<Appointment>> Between(Guid shopId, DateTimeOffset from, DateTimeOffset to, Guid? staffId, CancellationToken ct) =>
            Task.FromResult(Appointments.Where(a => a.ShopId == shopId && a.StartsAt < to && a.EndsAt > from && (staffId == null || a.StaffId == staffId)).ToList());
        public Task<List<Appointment>> StartingBetween(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult(Appointments.Where(a => a.StartsAt >= from && a.StartsAt < to && a.ReminderSentAt == null && a.CustomerId != null).ToList());
        public void Add(CommissionRule r) => RuleRows.Add(r);
        public Task<CommissionRule?> GetRule(Guid shopId, Guid? staffId, Guid? serviceId, CancellationToken ct) => Task.FromResult(RuleRows.FirstOrDefault(r => r.ShopId == shopId && r.StaffId == staffId && r.ServiceId == serviceId));
        public Task<List<CommissionRule>> Rules(Guid shopId, CancellationToken ct) => Task.FromResult(RuleRows.Where(r => r.ShopId == shopId).ToList());
        public Task<List<Appointment>> CompletedBetween(Guid shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult(Appointments.Where(a => a.ShopId == shopId && a.Status == AppointmentStatus.Completed && a.CompletedAt >= from && a.CompletedAt < to).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Tills : ITillAccessChecker
    {
        public HashSet<(Guid, Guid)> Allowed { get; } = [];
        public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct) => Task.FromResult(Allowed.Contains((deviceId, staffId)) ? Result.Ok() : Result.Fail("device.not_approved", "No."));
    }

    private sealed class People : IOrganisationAccess
    {
        public HashSet<(Guid, string)> Granted { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, perm)));
    }

    private sealed class Features : IShopFeatures
    {
        public HashSet<string> Off { get; } = [];
        public Task<bool> IsOn(Guid shopId, string feature, CancellationToken ct) => Task.FromResult(!Off.Contains(feature));
        public Task<ShopPackResponse> For(Guid shopId, CancellationToken ct) => Task.FromResult(new ShopPackResponse(shopId, "t", "T", 1, "slip.sale", [], "Test shop"));
    }

    private sealed class Customers : ICustomerDirectory
    {
        public Dictionary<Guid, CustomerInfo> All { get; } = [];
        public Task<CustomerInfo?> Get(Guid shopId, Guid customerId, CancellationToken ct) => Task.FromResult(All.GetValueOrDefault(customerId));
    }

    private sealed class FakeQueue : IMessageQueue
    {
        public List<QueueMessageRequest> Sent { get; } = [];
        public bool Fail { get; set; }
        public Task<Result<QueueMessageResult>> Queue(QueueMessageRequest r, CancellationToken ct)
        {
            if (Fail) return Task.FromResult(Result<QueueMessageResult>.Fail("bundle.exhausted", "Used up."));
            Sent.Add(r);
            return Task.FromResult(Result<QueueMessageResult>.Ok(new QueueMessageResult(Guid.NewGuid(), false)));
        }
    }

    private readonly Store _store = new();
    private readonly Tills _tills = new();
    private readonly People _people = new();
    private readonly Features _features = new();
    private readonly Customers _customers = new();
    private readonly FakeQueue _queue = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _till = Guid.NewGuid();
    private readonly Guid _cashier = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _anna = Guid.NewGuid();
    private readonly Guid _ben = Guid.NewGuid();
    private ServiceResponse _cut = default!;

    public BookingFlowTests()
    {
        _tills.Allowed.Add((_till, _cashier));
        _people.Granted.Add((_owner, "shop.settings"));
        _people.Granted.Add((_owner, "report.view"));
    }

    private ServiceHandlers Services => new(_store, _people, _tills, _features, _clock);
    private AppointmentHandlers Appointments => new(_store, _tills, _features, _clock);
    private ReminderHandler Reminders => new(_store, _customers, _queue, _clock);
    private CommissionHandlers Commission => new(_store, _people, _features, _clock);

    private async Task Setup(bool assignStaff = true)
    {
        _cut = (await Services.Handle(new SaveService(_shop, null, new(_owner, "Haircut", 45, 15_000, true, assignStaff ? [_anna, _ben] : null)), default)).Value!;
    }

    private static DateTimeOffset Tomorrow(int hour, int minute = 0) => new(2026, 10, 27, hour, minute, 0, TimeSpan.Zero);

    private Task<Result<AppointmentResponse>> Book(DateTimeOffset start, Guid? staff = null, Guid? id = null, Guid? customer = null, bool allowConflict = false, string name = "Thandi") =>
        Appointments.Handle(new BookAppointment(_shop, new(_till, _cashier, id ?? Guid.NewGuid(), _cut.Id, staff ?? _anna, customer, name, "0821234567", start, null, allowConflict)), default);

    // ---- services ----

    [Fact]
    public async Task The_owner_sets_up_services_and_who_can_do_them_and_a_till_lists_them()
    {
        await Setup();

        var list = (await Services.Handle(new ListServices(_shop, _till, _cashier), default)).Value!;

        Assert.Equal(("Haircut", 45, 15_000L), (list.Single().Name, list.Single().DurationMinutes, list.Single().PriceCents));
        Assert.Equal(new[] { _anna, _ben }.Order(), list.Single().StaffIds.Order());

        var changed = (await Services.Handle(new SaveService(_shop, _cut.Id, new(_owner, "Haircut and wash", 60, 18_000, true, [_anna])), default)).Value!;
        Assert.Equal([_anna], changed.StaffIds);
        Assert.Equal(60, changed.DurationMinutes);
    }

    [Fact]
    public async Task Only_the_owner_manages_services_and_the_pack_must_have_appointments()
    {
        Assert.Equal("permission.denied", (await Services.Handle(new SaveService(_shop, null, new(Guid.NewGuid(), "Cut", 30, 100, true, null)), default)).Error!.Code);
        Assert.Equal("service.not_found", (await Services.Handle(new SaveService(_shop, Guid.NewGuid(), new(_owner, "Cut", 30, 100, true, null)), default)).Error!.Code);
        _features.Off.Add("appointments");
        Assert.Equal("feature.not_enabled", (await Services.Handle(new SaveService(_shop, null, new(_owner, "Cut", 30, 100, true, null)), default)).Error!.Code);
        Assert.Equal("feature.not_enabled", (await Services.Handle(new ListServices(_shop, _till, _cashier), default)).Error!.Code);
    }

    // ---- booking ----

    [Fact]
    public async Task An_appointment_is_booked_with_the_services_duration_and_price_and_shows_in_the_calendar()
    {
        await Setup();

        var booked = (await Book(Tomorrow(10))).Value!;

        Assert.Equal(("Haircut", Tomorrow(10, 45), "booked", 15_000L), (booked.ServiceName, booked.EndsAt, booked.Status, booked.PriceCents));
        var day = (await Appointments.Handle(new ListAppointments(_shop, _till, _cashier, Tomorrow(0), Tomorrow(23), null), default)).Value!;
        Assert.Equal([booked.Id], day.Select(a => a.Id));
        Assert.Empty((await Appointments.Handle(new ListAppointments(_shop, _till, _cashier, Tomorrow(0), Tomorrow(23), _ben), default)).Value!);
    }

    [Fact]
    public async Task Booking_the_same_appointment_twice_makes_one()
    {
        await Setup();
        var id = Guid.NewGuid();

        await Book(Tomorrow(10), null, id);
        await Book(Tomorrow(10), null, id);

        Assert.Single(_store.Appointments);
    }

    [Fact]
    public async Task A_clash_for_the_same_staff_member_is_refused_online_but_another_person_or_back_to_back_is_fine()
    {
        await Setup();
        await Book(Tomorrow(10));

        var clash = await Book(Tomorrow(10, 30));
        Assert.Equal("appointment.conflict", clash.Error!.Code);
        Assert.Contains("Thandi is booked at 10:00", clash.Error.Message);
        Assert.True((await Book(Tomorrow(10, 30), _ben)).IsSuccess);
        Assert.True((await Book(Tomorrow(10, 45))).IsSuccess);   // starts as the first ends
        Assert.Equal(3, _store.Appointments.Count);
    }

    [Fact]
    public async Task An_appointment_booked_offline_that_clashes_is_saved_and_flagged_on_both_so_staff_can_sort_it_out()
    {
        await Setup();
        var first = (await Book(Tomorrow(10))).Value!;

        var offline = (await Book(Tomorrow(10, 15), null, null, null, allowConflict: true, name: "Sipho")).Value!;

        Assert.True(offline.HasConflict);
        Assert.True(_store.Appointments.Single(a => a.Id == first.Id).HasConflict);
    }

    [Fact]
    public async Task Cancelling_one_of_two_clashing_appointments_clears_the_flag_on_the_other()
    {
        await Setup();
        var first = (await Book(Tomorrow(10))).Value!;
        var second = (await Book(Tomorrow(10, 15), null, null, null, true)).Value!;

        await Appointments.Handle(new ChangeAppointment(_shop, second.Id, new(_till, _cashier, "cancel")), default);

        Assert.False(_store.Appointments.Single(a => a.Id == first.Id).HasConflict);
    }

    [Fact]
    public async Task Only_staff_who_do_the_service_can_be_booked_and_the_service_must_be_offered_and_the_time_not_long_gone()
    {
        await Setup();

        Assert.Equal("appointment.staff_cannot_do_service", (await Book(Tomorrow(10), Guid.NewGuid())).Error!.Code);
        Assert.Equal("service.not_found", (await Appointments.Handle(new BookAppointment(_shop, new(_till, _cashier, Guid.NewGuid(), Guid.NewGuid(), _anna, null, "T", null, Tomorrow(10), null)), default)).Error!.Code);
        Assert.Equal("appointment.in_the_past", (await Book(Start.AddDays(-3))).Error!.Code);
        await Services.Handle(new SaveService(_shop, _cut.Id, new(_owner, "Haircut", 45, 15_000, false, null)), default);
        Assert.Equal("service.not_found", (await Book(Tomorrow(10))).Error!.Code);   // switched off
    }

    [Fact]
    public async Task A_service_with_nobody_assigned_can_be_done_by_anyone()
    {
        await Setup(assignStaff: false);

        Assert.True((await Book(Tomorrow(10), Guid.NewGuid())).IsSuccess);
    }

    [Fact]
    public async Task A_till_that_is_not_approved_cannot_book()
    {
        await Setup();

        Assert.Equal("device.not_approved", (await Appointments.Handle(new BookAppointment(_shop, new(Guid.NewGuid(), _cashier, Guid.NewGuid(), _cut.Id, _anna, null, "T", null, Tomorrow(10), null)), default)).Error!.Code);
    }

    [Fact]
    public async Task Asking_for_a_calendar_needs_a_sensible_range()
    {
        await Setup();

        Assert.Equal("appointment.range_invalid", (await Appointments.Handle(new ListAppointments(_shop, _till, _cashier, Tomorrow(0), Tomorrow(0), null), default)).Error!.Code);
        Assert.Equal("appointment.range_invalid", (await Appointments.Handle(new ListAppointments(_shop, _till, _cashier, Tomorrow(0), Tomorrow(0).AddDays(70), null), default)).Error!.Code);
    }

    // ---- rescheduling and status ----

    [Fact]
    public async Task Rescheduling_moves_an_appointment_and_a_refused_move_leaves_it_where_it_was()
    {
        await Setup();
        var a = (await Book(Tomorrow(10))).Value!;
        await Book(Tomorrow(14));

        var refused = await Appointments.Handle(new RescheduleAppointment(_shop, a.Id, new(_till, _cashier, Tomorrow(14, 15), null)), default);
        Assert.Equal("appointment.conflict", refused.Error!.Code);
        Assert.Equal(Tomorrow(10), _store.Appointments.Single(x => x.Id == a.Id).StartsAt);

        var moved = (await Appointments.Handle(new RescheduleAppointment(_shop, a.Id, new(_till, _cashier, Tomorrow(16), null)), default)).Value!;
        Assert.Equal((Tomorrow(16), Tomorrow(16, 45)), (moved.StartsAt, moved.EndsAt));
    }

    [Fact]
    public async Task An_appointment_is_confirmed_completed_cancelled_or_marked_no_show_and_a_bad_action_is_refused()
    {
        await Setup();
        var a = (await Book(Tomorrow(10))).Value!;

        Task<Result<AppointmentResponse>> Do(string action) => Appointments.Handle(new ChangeAppointment(_shop, a.Id, new(_till, _cashier, action)), default);

        Assert.Equal("confirmed", (await Do("confirm")).Value!.Status);
        Assert.Equal("appointment.action_invalid", (await Do("teleport")).Error!.Code);
        Assert.Equal("completed", (await Do("complete")).Value!.Status);
        Assert.Equal("appointment.not_open", (await Do("cancel")).Error!.Code);
        Assert.Equal("appointment.not_found", (await Appointments.Handle(new ChangeAppointment(_shop, Guid.NewGuid(), new(_till, _cashier, "confirm")), default)).Error!.Code);
    }

    // ---- reminders ----

    private async Task<Guid> ConsentingCustomer(bool consent = true)
    {
        var id = Guid.NewGuid();
        _customers.All[id] = new CustomerInfo(id, "Thandi", "+27821234567", null, consent);
        return id;
    }

    [Fact]
    public async Task Tomorrows_appointment_gets_one_whatsapp_reminder_for_a_customer_who_agreed()
    {
        await Setup();
        var customer = await ConsentingCustomer();
        await Book(Tomorrow(7), null, null, customer);   // 23h ahead is inside the 24h window

        Assert.Equal(1, (await Reminders.Handle(new SendAppointmentReminders(), default)).Value);
        Assert.Equal(0, (await Reminders.Handle(new SendAppointmentReminders(), default)).Value);

        var message = Assert.Single(_queue.Sent);
        Assert.Equal(("appointment.reminder", "whatsapp", "+27821234567"), (message.TemplateCode, message.Channel, message.Destination));
        Assert.Equal(("Haircut", "09:00"), (message.Values["service"], message.Values["time"]));   // 07:00 UTC is 09:00 in South Africa
    }

    [Fact]
    public async Task A_customer_who_did_not_agree_or_is_only_a_name_is_never_messaged()
    {
        await Setup();
        await Book(Tomorrow(7), null, null, await ConsentingCustomer(consent: false));
        await Book(Tomorrow(7), _ben, null, null);

        Assert.Equal(0, (await Reminders.Handle(new SendAppointmentReminders(), default)).Value);
        Assert.Empty(_queue.Sent);
    }

    [Fact]
    public async Task A_reminder_that_cannot_be_sent_is_tried_again_on_the_next_round_and_a_cancelled_appointment_gets_none()
    {
        await Setup();
        var customer = await ConsentingCustomer();
        var booked = (await Book(Tomorrow(7), null, null, customer)).Value!;
        _queue.Fail = true;

        Assert.Equal(0, (await Reminders.Handle(new SendAppointmentReminders(), default)).Value);
        _queue.Fail = false;
        Assert.Equal(1, (await Reminders.Handle(new SendAppointmentReminders(), default)).Value);

        var other = (await Book(Tomorrow(7, 5), _ben, null, customer)).Value!;
        await Appointments.Handle(new ChangeAppointment(_shop, other.Id, new(_till, _cashier, "cancel")), default);
        Assert.Equal(0, (await Reminders.Handle(new SendAppointmentReminders(), default)).Value);
        _ = booked;
    }

    [Fact]
    public async Task Appointments_further_than_a_day_away_are_not_reminded_yet()
    {
        await Setup();
        await Book(Tomorrow(9), null, null, await ConsentingCustomer());

        Assert.Equal(0, (await Reminders.Handle(new SendAppointmentReminders(), default)).Value);
        _clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, (await Reminders.Handle(new SendAppointmentReminders(), default)).Value);
    }

    // ---- commission ----

    [Fact]
    public async Task The_owner_sets_rules_and_the_report_counts_only_completed_services_at_the_most_specific_rule()
    {
        await Setup();
        await Commission.Handle(new SaveCommissionRule(_shop, new(_owner, null, null, 10)), default);
        await Commission.Handle(new SaveCommissionRule(_shop, new(_owner, _anna, null, 30)), default);
        var a1 = (await Book(Tomorrow(9))).Value!;
        var a2 = (await Book(Tomorrow(11))).Value!;
        var b1 = (await Book(Tomorrow(9), _ben)).Value!;
        var cancelled = (await Book(Tomorrow(13))).Value!;
        foreach (var id in new[] { a1.Id, a2.Id, b1.Id }) await Appointments.Handle(new ChangeAppointment(_shop, id, new(_till, _cashier, "complete")), default);
        await Appointments.Handle(new ChangeAppointment(_shop, cancelled.Id, new(_till, _cashier, "cancel")), default);

        var rules = _store.RuleRows;
        var lines = CommissionReportBuilder.Lines(_store.Appointments.Where(a => a.Status == AppointmentStatus.Completed), rules);

        var anna = lines.Single(l => l.StaffId == _anna);
        var ben = lines.Single(l => l.StaffId == _ben);
        Assert.Equal((2, 30_000L, 9_000L), (anna.Services, anna.RevenueCents, anna.CommissionCents));   // 30% of R300
        Assert.Equal((1, 15_000L, 1_500L), (ben.Services, ben.RevenueCents, ben.CommissionCents));      // the 10% default
    }

    [Fact]
    public async Task The_commission_report_downloads_in_every_format_and_is_for_people_who_may_see_reports()
    {
        await Setup();
        var a = (await Book(Tomorrow(9))).Value!;
        await Appointments.Handle(new ChangeAppointment(_shop, a.Id, new(_till, _cashier, "complete")), default);
        _clock.Advance(TimeSpan.FromDays(2));

        foreach (var format in new[] { "csv", "xlsx", "pdf" })
            Assert.NotEmpty((await Commission.Handle(new GetCommissionReport(_shop, _owner, null, null, format), default)).Value!.Content);

        Assert.Equal("permission.denied", (await Commission.Handle(new GetCommissionReport(_shop, Guid.NewGuid(), null, null, "csv"), default)).Error!.Code);
        Assert.Equal("report.format_invalid", (await Commission.Handle(new GetCommissionReport(_shop, _owner, null, null, "doc"), default)).Error!.Code);
        Assert.Equal("report.range_invalid", (await Commission.Handle(new GetCommissionReport(_shop, _owner, new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 1), "csv"), default)).Error!.Code);
    }

    [Fact]
    public async Task A_rule_is_saved_once_per_scope_updated_in_place_and_checked()
    {
        await Setup();

        await Commission.Handle(new SaveCommissionRule(_shop, new(_owner, _anna, null, 20)), default);
        var again = (await Commission.Handle(new SaveCommissionRule(_shop, new(_owner, _anna, null, 25)), default)).Value!;

        Assert.Equal(25, again.Percent);
        Assert.Single(_store.RuleRows);
        Assert.Equal("commission.percent_invalid", (await Commission.Handle(new SaveCommissionRule(_shop, new(_owner, _anna, null, 101)), default)).Error!.Code);
        Assert.Equal("commission.percent_invalid", (await Commission.Handle(new SaveCommissionRule(_shop, new(_owner, _ben, null, -1)), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Commission.Handle(new SaveCommissionRule(_shop, new(Guid.NewGuid(), null, null, 10)), default)).Error!.Code);
        _features.Off.Add("commission");
        Assert.Equal("feature.not_enabled", (await Commission.Handle(new ListCommissionRules(_shop, _owner), default)).Error!.Code);
    }
}
