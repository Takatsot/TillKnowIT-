using Identity.Contracts;
using Messaging.Contracts;
using ShopCore.Application.Compliance;
using ShopCore.Application.Insights;
using ShopCore.Application.Reports;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Notifications;

public interface INotificationReadStore
{
    /// <summary>When sales of this shop were cancelled, since the given time.</summary>
    Task<List<DateTimeOffset>> CancellationTimes(Guid shopId, DateTimeOffset since, CancellationToken ct);

    /// <summary>The shop's alert settings (keys starting "alert."), as stored text.</summary>
    Task<Dictionary<string, string>> AlertSettings(Guid shopId, CancellationToken ct);
}

/// <summary>Messages to the owner: WhatsApp if they have a phone, otherwise email. No contact means no message, not an error.</summary>
internal static class OwnerContact
{
    public static (string Channel, string Destination)? Pick(ShopInfo? shop) => shop switch
    {
        { OwnerPhone: { Length: > 0 } phone } => ("whatsapp", phone),
        { OwnerEmail: { Length: > 0 } email } => ("email", email),
        _ => null,
    };
}

public sealed class OwnerMessenger(IShopDirectory shops, IMessageQueue queue)
{
    /// <summary>Queues one message to the shop's owner. Returns true when a new message was queued (false for a repeat or no contact).</summary>
    public async Task<bool> Send(Guid shopId, string template, Dictionary<string, string> values, string dedupeKey, CancellationToken ct)
    {
        var shop = await shops.Get(shopId, ct);
        if (OwnerContact.Pick(shop) is not var (channel, destination)) return false;

        values["shop"] = shop!.Name;
        var result = await queue.Queue(new QueueMessageRequest(template, shop.OwnerLanguage, channel, destination, values, shopId, dedupeKey), ct);
        return result.IsSuccess && !result.Value!.AlreadyQueued;
    }
}

/// <summary>E11-1: the evening summary for every verified shop, once a day, after 19:00 in the shop.</summary>
public sealed record SendDailySummaries : ICommand<int>;

internal sealed class SendDailySummariesHandler(
    IShopDirectory shops, IDispatcher dispatcher, IReportReadStore reports, INotificationReadStore notifications, OwnerMessenger messenger, TimeProvider clock)
    : ICommandHandler<SendDailySummaries, int>
{
    public async Task<Result<int>> Handle(SendDailySummaries command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!DailySummaryRules.IsDue(now)) return Result<int>.Ok(0);

        var today = BusinessDay.Of(now);
        var queued = 0;
        foreach (var shopId in await shops.VerifiedShopIds(ct))
        {
            var insights = (await dispatcher.Ask(new GetInsights(shopId), ct)).Value;
            if (insights is null) continue;
            var cashUps = await reports.CashUps(shopId, today, today, ct);
            var cancelled = (await notifications.CancellationTimes(shopId, BusinessDay.Start(today), ct)).Count;

            var values = new Dictionary<string, string>
            {
                ["sales"] = "R" + (insights.Today.SalesCents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                ["profit"] = "R" + (insights.Today.ProfitCents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                ["customers"] = insights.Today.Customers.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["low"] = insights.RunningLow.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["expiring"] = insights.Expiring.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["cashup"] = DailySummaryRules.CashUpText(cashUps.Select(c => c.DifferenceCents).ToList()),
                ["cancellations"] = cancelled.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            if (await messenger.Send(shopId, "daily.summary", values, DailySummaryRules.Key(shopId, today), ct)) queued++;
        }
        return Result<int>.Ok(queued);
    }
}

/// <summary>E11-2: too many cancellations in an hour, or a cash-up that is short by more than the owner's level.</summary>
public sealed record EvaluateLossAlerts : ICommand<int>;

internal sealed class EvaluateLossAlertsHandler(
    IShopDirectory shops, INotificationReadStore notifications, IReportReadStore reports, OwnerMessenger messenger, TimeProvider clock)
    : ICommandHandler<EvaluateLossAlerts, int>
{
    public async Task<Result<int>> Handle(EvaluateLossAlerts command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = BusinessDay.Of(now);
        var queued = 0;

        foreach (var shopId in await shops.VerifiedShopIds(ct))
        {
            var settings = AlertSettings.From(await notifications.AlertSettings(shopId, ct));

            var cancellations = await notifications.CancellationTimes(shopId, now.AddHours(-1), ct);
            if (LossAlertRules.TooManyCancellations(cancellations, now, settings.CancellationsPerHour)
                && await messenger.Send(shopId, "alert.loss",
                    new() { ["message"] = $"{cancellations.Count} sales were cancelled in the last hour." },
                    LossAlertRules.CancellationKey(shopId, now), ct))
                queued++;

            foreach (var cashUp in await reports.CashUps(shopId, today, today, ct))
                if (LossAlertRules.CashUpTooShort(cashUp.DifferenceCents, settings.CashUpShortCents)
                    && await messenger.Send(shopId, "alert.loss",
                        new() { ["message"] = $"The cash-up on {cashUp.Day:yyyy-MM-dd} is short by R{(-cashUp.DifferenceCents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}." },
                        LossAlertRules.CashUpKey(cashUp.CashUpId), ct))
                    queued++;
        }
        return Result<int>.Ok(queued);
    }
}

/// <summary>A till that has not synced for 48 hours: tell the owner once per outage.</summary>
public sealed record AlertStaleDevices : ICommand<int>;

internal sealed class AlertStaleDevicesHandler(IDeviceDirectory devices, OwnerMessenger messenger, TimeProvider clock)
    : ICommandHandler<AlertStaleDevices, int>
{
    public async Task<Result<int>> Handle(AlertStaleDevices command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var queued = 0;
        foreach (var device in await devices.NotSeenSince(now.AddHours(-LossAlertRules.OfflineAlertHours), ct))
            if (await messenger.Send(device.ShopId, "device.offline",
                    new() { ["device"] = device.Name, ["hours"] = ((int)(now - device.LastSeenAt).TotalHours).ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    LossAlertRules.OfflineKey(device.DeviceId, device.LastSeenAt), ct))
                queued++;
        return Result<int>.Ok(queued);
    }
}

/// <summary>E9-1: licences and certificates 30 days, 7 days and 0 days (expired) from their expiry date.</summary>
public sealed record SendComplianceReminders : ICommand<int>;

internal sealed class SendComplianceRemindersHandler(IDispatcher dispatcher, OwnerMessenger messenger, TimeProvider clock)
    : ICommandHandler<SendComplianceReminders, int>
{
    public async Task<Result<int>> Handle(SendComplianceReminders command, CancellationToken ct)
    {
        var today = BusinessDay.Of(clock.GetUtcNow());
        var queued = 0;
        foreach (var due in (await dispatcher.Ask(new FindDueComplianceReminders(), ct)).Value ?? [])
        {
            var values = new Dictionary<string, string>
            {
                ["document"] = due.TypeCode.Replace('_', ' '),
                ["date"] = due.ExpiresOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                ["days"] = (ComplianceRules.DaysLeft(due.ExpiresOn, today) ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            if (await messenger.Send(due.ShopId, $"compliance.reminder.{due.Reminder}", values, due.DedupeKey, ct)) queued++;
        }
        return Result<int>.Ok(queued);
    }
}
