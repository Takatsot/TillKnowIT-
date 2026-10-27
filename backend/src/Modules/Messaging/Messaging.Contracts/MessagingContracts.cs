using TillKnowIT.SharedKernel;

namespace Messaging.Contracts;

/// <summary>
/// Channel is whatsapp, sms, email or push. Values fill the {placeholders} in the template. A DedupeKey makes the
/// request safe to repeat: the same key is only ever queued once (for example "daily:{shop}:2026-10-26").
/// </summary>
public sealed record QueueMessageRequest(
    string TemplateCode, string Language, string Channel, string Destination, Dictionary<string, string> Values,
    Guid? ShopId = null, string? DedupeKey = null);

public sealed record QueueMessageResult(Guid MessageId, bool AlreadyQueued);

/// <summary>How every other module sends a message. It only queues; the worker delivers and retries.</summary>
public interface IMessageQueue
{
    Task<Result<QueueMessageResult>> Queue(QueueMessageRequest request, CancellationToken ct);
}

/// <summary>Checks a phone number or email for a channel before anyone is promised a message.</summary>
public interface IDestinationChecker
{
    /// <summary>Returns the cleaned destination (for example +27821234567), or why it cannot be used.</summary>
    Result<string> Check(string channel, string? destination);
}
