using System.Text.RegularExpressions;
using TillKnowIT.SharedKernel;

namespace Messaging.Domain;

public enum MessageChannel { Whatsapp, Sms, Email, Push }

public static class MessageChannels
{
    public static bool TryParse(string? value, out MessageChannel channel)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "whatsapp": channel = MessageChannel.Whatsapp; return true;
            case "sms": channel = MessageChannel.Sms; return true;
            case "email": channel = MessageChannel.Email; return true;
            case "push": channel = MessageChannel.Push; return true;
            default: channel = default; return false;
        }
    }
}

/// <summary>Templates are data, one row per code, language and channel. English is the fallback.</summary>
public sealed class MessageTemplate : VersionedEntity
{
    private MessageTemplate() { }

    public MessageTemplate(string code, string language, MessageChannel channel, string body, string? subject = null)
    {
        Code = code;
        Language = language;
        Channel = channel;
        Body = body;
        Subject = subject;
    }

    public string Code { get; private set; } = default!;
    public string Language { get; private set; } = default!;
    public MessageChannel Channel { get; private set; }
    public string? Subject { get; private set; }
    public string Body { get; private set; } = default!;
}

public static partial class TemplateRenderer
{
    /// <summary>Fills {placeholders}. A missing value is an error: a message that says "{total}" must never reach a customer.</summary>
    public static Result<string> Render(string text, IReadOnlyDictionary<string, string> values)
    {
        var missing = new List<string>();
        var rendered = Placeholder().Replace(text, m =>
        {
            if (values.TryGetValue(m.Groups[1].Value, out var v)) return v;
            missing.Add(m.Groups[1].Value);
            return m.Value;
        });
        return missing.Count == 0
            ? Result<string>.Ok(rendered)
            : Result<string>.Fail("template.value_missing", $"No value for: {string.Join(", ", missing.Distinct())}.");
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}

public static partial class Destinations
{
    /// <summary>Returns the cleaned destination: +27 phone numbers for WhatsApp and SMS, a trimmed email, or the person id for push.</summary>
    public static Result<string> Normalise(MessageChannel channel, string? destination)
    {
        var d = destination?.Trim() ?? "";
        switch (channel)
        {
            case MessageChannel.Whatsapp or MessageChannel.Sms:
                var digits = new string(d.Where(c => char.IsDigit(c) || c == '+').ToArray());
                if (digits.StartsWith("00", StringComparison.Ordinal)) digits = "+" + digits[2..];
                if (digits.StartsWith('0')) digits = "+27" + digits[1..];
                else if (digits.Length > 0 && !digits.StartsWith('+')) digits = "+" + digits;
                return Phone().IsMatch(digits) ? Result<string>.Ok(digits) : Result<string>.Fail("destination.phone_invalid", "That phone number is not valid.");
            case MessageChannel.Email:
                return d.Length <= 320 && Email().IsMatch(d) ? Result<string>.Ok(d) : Result<string>.Fail("destination.email_invalid", "That email address is not valid.");
            default:
                return Guid.TryParse(d, out var id) ? Result<string>.Ok(id.ToString()) : Result<string>.Fail("destination.push_invalid", "A push message goes to a person id.");
        }
    }

    [GeneratedRegex(@"^\+\d{9,15}$")]
    private static partial Regex Phone();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();
}

public static class MessageCosts
{
    private const string Gsm7Basic = "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
    private const string Gsm7Extended = "^{}\\[~]|€";

    /// <summary>
    /// How many SMS parts a text costs. Plain text is 160 characters (153 per part when joined); any other character,
    /// such as an emoji or a Cyrillic letter, makes it 70 (67 per part). Some characters count twice.
    /// </summary>
    public static int SmsSegments(string text)
    {
        if (text.Length == 0) return 1;
        var gsm = text.All(c => Gsm7Basic.Contains(c) || Gsm7Extended.Contains(c));
        if (gsm)
        {
            var length = text.Sum(c => Gsm7Extended.Contains(c) ? 2 : 1);
            return length <= 160 ? 1 : (int)Math.Ceiling(length / 153.0);
        }
        return text.Length <= 70 ? 1 : (int)Math.Ceiling(text.Length / 67.0);
    }
}

public enum MessageStatus { Queued, Sent, Failed }

public sealed class OutboundMessage : Entity
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

    private OutboundMessage() { }

    public static Result<OutboundMessage> Queue(MessageChannel channel, string destination, string? subject, string body, string templateCode,
        Guid? shopId, string? dedupeKey, DateTimeOffset now)
    {
        var dest = Destinations.Normalise(channel, destination);
        if (!dest.IsSuccess) return Result<OutboundMessage>.Fail(dest.Error!.Code, dest.Error.Message);
        if (string.IsNullOrWhiteSpace(body)) return Result<OutboundMessage>.Fail("message.empty", "The message is empty.");
        if (channel == MessageChannel.Sms && MessageCosts.SmsSegments(body) > 4)
            return Result<OutboundMessage>.Fail("message.too_long", "That text is too long for an SMS.");
        if (dedupeKey is { Length: > 200 }) return Result<OutboundMessage>.Fail("message.dedupe_invalid", "The dedupe key is too long.");

        var message = new OutboundMessage
        {
            Channel = channel, Destination = dest.Value!, Subject = subject, Body = body, TemplateCode = templateCode,
            ShopId = shopId, DedupeKey = dedupeKey, NextAttemptAt = now,
        };
        return Result<OutboundMessage>.Ok(message);
    }

    public MessageChannel Channel { get; private set; }
    public string Destination { get; private set; } = default!;
    public string? Subject { get; private set; }
    public string Body { get; private set; } = default!;
    public string TemplateCode { get; private set; } = default!;
    public Guid? ShopId { get; private set; }
    public string? DedupeKey { get; private set; }
    public MessageStatus Status { get; private set; } = MessageStatus.Queued;
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? ProviderRef { get; private set; }
    public string? Error { get; private set; }
    public long CostCents { get; private set; }

    public void MarkSent(DateTimeOffset now, long costCents, string? providerRef)
    {
        Status = MessageStatus.Sent;
        SentAt = now;
        CostCents = costCents;
        ProviderRef = providerRef;
        Error = null;
    }

    /// <summary>A failed try. A permanent failure (a number that does not exist) stops at once; others retry with backoff, then give up.</summary>
    public void RecordFailure(DateTimeOffset now, string error, bool permanent)
    {
        Attempts++;
        Error = error.Length > 500 ? error[..500] : error;
        if (permanent || Attempts >= MaxAttempts)
        {
            Status = MessageStatus.Failed;
            return;
        }
        NextAttemptAt = now + Backoff[Math.Min(Attempts, Backoff.Length) - 1];
    }
}
