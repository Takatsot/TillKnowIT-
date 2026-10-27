using Identity.Contracts;
using Messaging.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Compliance;
using ShopCore.Application.Insights;
using ShopCore.Application.Notifications;
using ShopCore.Application.Reports;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public class NotificationTests
{
    // 26 Oct 2026, 20:00 in the shop (18:00 UTC): after the evening summary time.
    private static readonly DateTimeOffset Evening = new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 26);

    // ---- rules ----

    [Fact]
    public void Alert_levels_come_from_settings_and_bad_values_fall_back_to_the_defaults()
    {
        Assert.Equal(new AlertSettings(3, 5000), AlertSettings.From(new Dictionary<string, string>()));
        Assert.Equal(new AlertSettings(5, 2000), AlertSettings.From(new Dictionary<string, string> { ["alert.cancellations_per_hour"] = "5", ["alert.cashup_short_cents"] = "2000" }));
        Assert.Equal(new AlertSettings(3, 5000), AlertSettings.From(new Dictionary<string, string> { ["alert.cancellations_per_hour"] = "lots", ["alert.cashup_short_cents"] = "-10" }));
        Assert.Equal(new AlertSettings(3, 5000), AlertSettings.From(new Dictionary<string, string> { ["alert.cancellations_per_hour"] = "0", ["alert.cashup_short_cents"] = "0" }));
    }

    [Fact]
    public void Cancellations_count_only_inside_the_last_hour_and_the_limit_is_inclusive()
    {
        var now = Evening;
        DateTimeOffset[] times = [now.AddMinutes(-5), now.AddMinutes(-30), now.AddMinutes(-59), now.AddHours(-1), now.AddHours(-3)];

        Assert.True(LossAlertRules.TooManyCancellations(times, now, 3));      // three inside the hour (exactly 60 minutes ago is out)
        Assert.False(LossAlertRules.TooManyCancellations(times, now, 4));
        Assert.False(LossAlertRules.TooManyCancellations([], now, 1));
    }

    [Fact]
    public void A_cash_up_is_an_alert_only_when_short_by_at_least_the_level()
    {
        Assert.True(LossAlertRules.CashUpTooShort(-5000, 5000));
        Assert.True(LossAlertRules.CashUpTooShort(-9000, 5000));
        Assert.False(LossAlertRules.CashUpTooShort(-4999, 5000));
        Assert.False(LossAlertRules.CashUpTooShort(8000, 5000));              // over is not a loss alert
    }

    [Fact]
    public void Alert_keys_group_a_burst_into_one_message_and_a_new_outage_into_a_new_one()
    {
        var shop = Guid.NewGuid();
        Assert.Equal(LossAlertRules.CancellationKey(shop, Evening), LossAlertRules.CancellationKey(shop, Evening.AddMinutes(20)));
        Assert.NotEqual(LossAlertRules.CancellationKey(shop, Evening), LossAlertRules.CancellationKey(shop, Evening.AddHours(1)));

        var device = Guid.NewGuid();
        Assert.Equal(LossAlertRules.OfflineKey(device, Evening), LossAlertRules.OfflineKey(device, Evening));
        Assert.NotEqual(LossAlertRules.OfflineKey(device, Evening), LossAlertRules.OfflineKey(device, Evening.AddDays(9)));
    }

    [Theory]
    [InlineData(16, 59, false)]    // 18:59 in the shop
    [InlineData(17, 0, true)]      // 19:00 in the shop
    [InlineData(21, 59, true)]     // 23:59 in the shop
    [InlineData(22, 30, false)]    // 00:30: a new day, its summary is not due until 19:00
    public void The_summary_is_due_from_19_00_shop_time(int utcHour, int minute, bool due) =>
        Assert.Equal(due, DailySummaryRules.IsDue(new DateTimeOffset(2026, 10, 26, utcHour, minute, 0, TimeSpan.Zero)));

    [Fact]
    public void The_cash_up_line_reads_in_plain_words()
    {
        Assert.Equal("not done today", DailySummaryRules.CashUpText([]));
        Assert.Equal("balanced", DailySummaryRules.CashUpText([0]));
        Assert.Equal("short by R1.50", DailySummaryRules.CashUpText([-100, -50]));
        Assert.Equal("over by R2.00", DailySummaryRules.CashUpText([200]));
    }

    // ---- fakes ----

    private sealed class FakeShops : IShopDirectory, IDeviceDirectory
    {
        public Dictionary<Guid, ShopInfo> Shops { get; } = [];
        public List<StaleDevice> Stale { get; } = [];
        public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) => Task.FromResult(Shops.GetValueOrDefault(shopId));
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(Shops.Values.Where(s => s.Status == "verified").Select(s => s.ShopId).ToList());
        public Task<List<StaleDevice>> NotSeenSince(DateTimeOffset cutoff, CancellationToken ct) => Task.FromResult(Stale.Where(d => d.LastSeenAt < cutoff).ToList());
    }

    private sealed class FakeQueue : IMessageQueue
    {
        public List<QueueMessageRequest> Sent { get; } = [];
        public Task<Result<QueueMessageResult>> Queue(QueueMessageRequest r, CancellationToken ct)
        {
            if (r.DedupeKey is not null && Sent.Any(s => s.DedupeKey == r.DedupeKey))
                return Task.FromResult(Result<QueueMessageResult>.Ok(new QueueMessageResult(Guid.NewGuid(), true)));
            Sent.Add(r);
            return Task.FromResult(Result<QueueMessageResult>.Ok(new QueueMessageResult(Guid.NewGuid(), false)));
        }
    }

    private sealed class FakeNotificationStore : INotificationReadStore
    {
        public List<DateTimeOffset> Cancellations { get; } = [];
        public Dictionary<string, string> Settings { get; } = [];
        public Task<List<DateTimeOffset>> CancellationTimes(Guid shopId, DateTimeOffset since, CancellationToken ct) => Task.FromResult(Cancellations.Where(c => c >= since).ToList());
        public Task<Dictionary<string, string>> AlertSettings(Guid shopId, CancellationToken ct) => Task.FromResult(Settings);
    }

    private sealed class FakeReports : IReportReadStore
    {
        public List<CashUpRow> Rows { get; } = [];
        public Task<List<CashUpRow>> CashUps(Guid shopId, DateOnly from, DateOnly to, CancellationToken ct) => Task.FromResult(Rows);
    }

    private sealed class FakeDispatcher : IDispatcher
    {
        public InsightsResponse Insights { get; set; } = new(new(1, 8550, 1990, 7, 1221), new(7, 0, 0, 0, 0), new(30, 0, 0, 0, 0), [], [Row("Bread"), Row("Milk")], [], [Row("Yoghurt")]);
        public List<DueComplianceReminder> Due { get; } = [];

        private static StockRow Row(string name) => new(Guid.NewGuid(), name, null, 1, 5, null, 1, 5, []);

        public Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<TResult>> Ask<TResult>(IQuery<TResult> query, CancellationToken ct = default) => query switch
        {
            GetInsights => Task.FromResult((Result<TResult>)(object)Result<InsightsResponse>.Ok(Insights)),
            FindDueComplianceReminders => Task.FromResult((Result<TResult>)(object)Result<List<DueComplianceReminder>>.Ok(Due)),
            _ => throw new NotSupportedException(query.GetType().Name),
        };
    }

    private readonly FakeShops _shops = new();
    private readonly FakeQueue _queue = new();
    private readonly FakeNotificationStore _store = new();
    private readonly FakeReports _reports = new();
    private readonly FakeDispatcher _dispatcher = new();
    private readonly Guid _shop = Guid.NewGuid();

    public NotificationTests() => _shops.Shops[_shop] = new ShopInfo(_shop, "Mama's Spaza", "verified", Guid.NewGuid(), "0821234567", "owner@shop.co.za", "zu");

    private OwnerMessenger Messenger() => new(_shops, _queue);
    private static FakeTimeProvider At(DateTimeOffset t) => new(t);

    // ---- daily summary ----

    [Fact]
    public async Task The_summary_goes_to_the_owner_after_19_00_with_the_days_numbers()
    {
        _reports.Rows.Add(new CashUpRow(Today, Guid.NewGuid(), 5000, 8550, 8450, -100, Guid.NewGuid()));
        _store.Cancellations.Add(Evening.AddHours(-2));
        var handler = new SendDailySummariesHandler(_shops, _dispatcher, _reports, _store, Messenger(), At(Evening));

        var count = (await handler.Handle(new SendDailySummaries(), default)).Value;

        Assert.Equal(1, count);
        var sent = Assert.Single(_queue.Sent);
        Assert.Equal("daily.summary", sent.TemplateCode);
        Assert.Equal("whatsapp", sent.Channel);
        Assert.Equal("0821234567", sent.Destination);
        Assert.Equal("zu", sent.Language);
        Assert.Equal("Mama's Spaza", sent.Values["shop"]);
        Assert.Equal("R85.50", sent.Values["sales"]);
        Assert.Equal("R19.90", sent.Values["profit"]);
        Assert.Equal("7", sent.Values["customers"]);
        Assert.Equal("2", sent.Values["low"]);
        Assert.Equal("1", sent.Values["expiring"]);
        Assert.Equal("short by R1.00", sent.Values["cashup"]);
        Assert.Equal("1", sent.Values["cancellations"]);
        Assert.Equal(DailySummaryRules.Key(_shop, Today), sent.DedupeKey);
    }

    [Fact]
    public async Task The_summary_is_not_sent_before_19_00_and_never_twice_in_a_day()
    {
        var early = new DateTimeOffset(2026, 10, 26, 16, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, (await new SendDailySummariesHandler(_shops, _dispatcher, _reports, _store, Messenger(), At(early)).Handle(new SendDailySummaries(), default)).Value);

        var handler = new SendDailySummariesHandler(_shops, _dispatcher, _reports, _store, Messenger(), At(Evening));
        Assert.Equal(1, (await handler.Handle(new SendDailySummaries(), default)).Value);
        Assert.Equal(0, (await handler.Handle(new SendDailySummaries(), default)).Value);
        Assert.Single(_queue.Sent);
    }

    [Fact]
    public async Task An_owner_without_a_phone_gets_email_and_one_with_no_contact_is_skipped_quietly()
    {
        var emailOnly = Guid.NewGuid();
        var noContact = Guid.NewGuid();
        _shops.Shops.Clear();
        _shops.Shops[emailOnly] = new ShopInfo(emailOnly, "Email shop", "verified", null, null, "o@x.co.za", "en");
        _shops.Shops[noContact] = new ShopInfo(noContact, "Silent shop", "verified", null, null, null, "en");
        _shops.Shops[Guid.NewGuid()] = new ShopInfo(Guid.NewGuid(), "Pending shop", "pending", null, "0821111111", null, "en");

        var count = (await new SendDailySummariesHandler(_shops, _dispatcher, _reports, _store, Messenger(), At(Evening)).Handle(new SendDailySummaries(), default)).Value;

        Assert.Equal(1, count);
        Assert.Equal("email", _queue.Sent.Single().Channel);
        Assert.Equal("o@x.co.za", _queue.Sent.Single().Destination);
    }

    // ---- loss alerts ----

    private EvaluateLossAlertsHandler Alerts(DateTimeOffset at) => new(_shops, _store, _reports, Messenger(), At(at));

    [Fact]
    public async Task Three_cancellations_in_an_hour_alert_the_owner_once_for_that_hour()
    {
        _store.Cancellations.AddRange([Evening.AddMinutes(-5), Evening.AddMinutes(-20), Evening.AddMinutes(-40)]);

        Assert.Equal(1, (await Alerts(Evening).Handle(new EvaluateLossAlerts(), default)).Value);
        Assert.Equal(0, (await Alerts(Evening.AddMinutes(10)).Handle(new EvaluateLossAlerts(), default)).Value);

        Assert.Equal("3 sales were cancelled in the last hour.", _queue.Sent.Single().Values["message"]);
        Assert.Equal("alert.loss", _queue.Sent.Single().TemplateCode);
    }

    [Fact]
    public async Task The_owners_own_level_decides_when_cancellations_alert()
    {
        _store.Cancellations.AddRange([Evening.AddMinutes(-5), Evening.AddMinutes(-20)]);
        Assert.Equal(0, (await Alerts(Evening).Handle(new EvaluateLossAlerts(), default)).Value);      // default level is 3

        _store.Settings["alert.cancellations_per_hour"] = "2";
        Assert.Equal(1, (await Alerts(Evening).Handle(new EvaluateLossAlerts(), default)).Value);
    }

    [Fact]
    public async Task A_short_cash_up_alerts_once_and_a_small_difference_does_not()
    {
        var shortId = Guid.NewGuid();
        _reports.Rows.Add(new CashUpRow(Today, Guid.NewGuid(), 5000, 10000, 4000, -6000, shortId));
        _reports.Rows.Add(new CashUpRow(Today, Guid.NewGuid(), 5000, 10000, 9900, -100, Guid.NewGuid()));

        Assert.Equal(1, (await Alerts(Evening).Handle(new EvaluateLossAlerts(), default)).Value);
        Assert.Equal(0, (await Alerts(Evening.AddMinutes(5)).Handle(new EvaluateLossAlerts(), default)).Value);

        Assert.Equal("The cash-up on 2026-10-26 is short by R60.00.", _queue.Sent.Single().Values["message"]);
        Assert.Equal(LossAlertRules.CashUpKey(shortId), _queue.Sent.Single().DedupeKey);
    }

    // ---- offline tills ----

    [Fact]
    public async Task A_till_silent_for_48_hours_alerts_the_owner_once_per_outage()
    {
        var device = Guid.NewGuid();
        _shops.Stale.Add(new StaleDevice(_shop, device, "Counter phone", Evening.AddHours(-50)));
        _shops.Stale.Add(new StaleDevice(_shop, Guid.NewGuid(), "Fine till", Evening.AddHours(-10)));
        var handler = new AlertStaleDevicesHandler(_shops, Messenger(), At(Evening));

        Assert.Equal(1, (await handler.Handle(new AlertStaleDevices(), default)).Value);
        Assert.Equal(0, (await handler.Handle(new AlertStaleDevices(), default)).Value);

        var sent = _queue.Sent.Single();
        Assert.Equal("device.offline", sent.TemplateCode);
        Assert.Equal("Counter phone", sent.Values["device"]);
        Assert.Equal("50", sent.Values["hours"]);
    }

    // ---- compliance reminders ----

    [Fact]
    public async Task Each_due_reminder_is_sent_with_its_own_template_and_only_once()
    {
        var doc30 = Guid.NewGuid();
        var doc0 = Guid.NewGuid();
        _dispatcher.Due.Add(new DueComplianceReminder(_shop, doc30, "trading_licence", Today.AddDays(30), 30, ComplianceRules.DedupeKey(doc30, 30)));
        _dispatcher.Due.Add(new DueComplianceReminder(_shop, doc0, "health_certificate", Today.AddDays(-2), 0, ComplianceRules.DedupeKey(doc0, 0)));
        var handler = new SendComplianceRemindersHandler(_dispatcher, Messenger(), At(Evening));

        Assert.Equal(2, (await handler.Handle(new SendComplianceReminders(), default)).Value);
        Assert.Equal(0, (await handler.Handle(new SendComplianceReminders(), default)).Value);

        Assert.Equal(["compliance.reminder.30", "compliance.reminder.0"], _queue.Sent.Select(s => s.TemplateCode));
        Assert.Equal("trading licence", _queue.Sent[0].Values["document"]);
        Assert.Equal("30", _queue.Sent[0].Values["days"]);
    }
}
