using Messaging.Domain;
using TillKnowIT.SharedKernel;

namespace Messaging.Infrastructure.Persistence;

/// <summary>
/// The message templates every environment starts with. English only: translators add the other languages as data,
/// and until they do the English text is sent, so no message is ever blank.
/// </summary>
internal static class MessagingSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Summary =
        "{shop} today: sales {sales}, profit {profit}, {customers} customers. Low stock: {low}. Expiring soon: {expiring}. Cash-up: {cashup}. Cancellations: {cancellations}.";

    private static readonly (string Code, MessageChannel[] Channels, string? Subject, string Body)[] Templates =
    [
        // Slips on request: WhatsApp, SMS and email.
        ("slip.sale", [MessageChannel.Whatsapp], null, "Thank you for shopping at {shop}.\n{lines}\nTotal: {total}\n{date}\nYour slip: {url}"),
        ("slip.sale", [MessageChannel.Sms], null, "{shop}: {total} paid on {date}. Slip: {url}"),
        ("slip.sale", [MessageChannel.Email], "Your slip from {shop}", "Thank you for shopping at {shop}.\n\n{lines}\n\nTotal: {total}\nDate: {date}\n\nView your slip: {url}"),

        // Messages to the owner.
        ("daily.summary", [MessageChannel.Whatsapp, MessageChannel.Push], null, Summary),
        ("daily.summary", [MessageChannel.Email], "Daily summary for {shop}", Summary),
        ("alert.loss", [MessageChannel.Whatsapp, MessageChannel.Push, MessageChannel.Email], null, "Alert for {shop}: {message}"),
        ("device.offline", [MessageChannel.Whatsapp, MessageChannel.Push, MessageChannel.Email], null, "{shop}: the till '{device}' has not synced for {hours} hours."),
        ("compliance.reminder.30", [MessageChannel.Whatsapp, MessageChannel.Push, MessageChannel.Email], null, "{shop}: your {document} expires on {date}. Please renew it in good time."),
        ("compliance.reminder.7", [MessageChannel.Whatsapp, MessageChannel.Push, MessageChannel.Email], null, "{shop}: your {document} expires in {days} days, on {date}. Renew it now."),
        ("compliance.reminder.0", [MessageChannel.Whatsapp, MessageChannel.Push, MessageChannel.Email], null, "{shop}: your {document} expired on {date}. Renew it as soon as possible."),
    ];

    public static object[] Rows() => Templates
        .SelectMany(t => t.Channels.Select(channel => (object)new
        {
            Id = DeterministicGuid.From($"messaging.template:{t.Code}:en:{channel}"),
            Code = t.Code,
            Language = "en",
            Channel = channel,
            Subject = t.Subject,
            Body = t.Body,
            Version = 1,
            CreatedAt = SeededAt,
        }))
        .ToArray();
}
