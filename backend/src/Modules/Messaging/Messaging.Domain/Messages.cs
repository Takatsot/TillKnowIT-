using TillKnowIT.SharedKernel;

namespace Messaging.Domain;

public enum MessageChannel { Whatsapp, Sms, Email, Push }

/// <summary>Templates are data, one row per code, language and channel.</summary>
public sealed class MessageTemplate : VersionedEntity
{
    private MessageTemplate() { }

    public MessageTemplate(string code, string language, MessageChannel channel, string body)
    {
        Code = code;
        Language = language;
        Channel = channel;
        Body = body;
    }

    public string Code { get; private set; } = default!;
    public string Language { get; private set; } = default!;
    public MessageChannel Channel { get; private set; }
    public string Body { get; private set; } = default!;
}

public enum MessageStatus { Queued, Sent, Failed }

public sealed class OutboundMessage : Entity
{
    private OutboundMessage() { }

    public OutboundMessage(MessageChannel channel, string destination, string body)
    {
        Channel = channel;
        Destination = destination;
        Body = body;
    }

    public MessageChannel Channel { get; private set; }
    public string Destination { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public MessageStatus Status { get; private set; } = MessageStatus.Queued;
    public long CostCents { get; private set; }

    public void MarkSent(long costCents)
    {
        Status = MessageStatus.Sent;
        CostCents = costCents;
    }

    public void MarkFailed() => Status = MessageStatus.Failed;
}
