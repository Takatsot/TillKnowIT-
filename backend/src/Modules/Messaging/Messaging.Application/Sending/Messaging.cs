using Messaging.Contracts;
using Messaging.Domain;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Messaging.Application.Sending;

public interface IMessagingStore
{
    /// <summary>The template for this language, falling back to English. Null when neither exists.</summary>
    Task<MessageTemplate?> FindTemplate(string code, string language, MessageChannel channel, CancellationToken ct);

    Task<OutboundMessage?> FindByDedupeKey(string dedupeKey, CancellationToken ct);
    Task Add(OutboundMessage message, CancellationToken ct);
    Task<List<OutboundMessage>> Due(DateTimeOffset now, int take, CancellationToken ct);
    Task Save(CancellationToken ct);
}

/// <summary>One provider per channel (WhatsApp Business, an SMS gateway, an email service, push). None is connected yet.</summary>
public interface IMessageChannelProvider
{
    MessageChannel Channel { get; }
    Task<SendOutcome> Send(OutboundMessage message, CancellationToken ct);
}

/// <summary>Permanent means retrying cannot help, for example a number that does not exist.</summary>
public sealed record SendOutcome(bool Success, bool Permanent = false, string? ProviderRef = null, string? Error = null);

/// <summary>What each message costs us. The rates here are placeholders until the provider contracts are signed.</summary>
public interface IMessagePricing
{
    long CostCents(MessageChannel channel, string body);
}

internal sealed class DefaultMessagePricing : IMessagePricing
{
    public long CostCents(MessageChannel channel, string body) => channel switch
    {
        MessageChannel.Sms => 30L * MessageCosts.SmsSegments(body),
        MessageChannel.Whatsapp => 45,
        MessageChannel.Email => 2,
        _ => 0,
    };
}

internal sealed class MessageQueue(IMessagingStore store, TimeProvider clock) : IMessageQueue
{
    public async Task<Result<QueueMessageResult>> Queue(QueueMessageRequest request, CancellationToken ct)
    {
        if (!MessageChannels.TryParse(request.Channel, out var channel))
            return Result<QueueMessageResult>.Fail("message.channel_invalid", "Channel must be whatsapp, sms, email or push.");

        // The same key is only ever queued once, so a retry or a restart cannot send a message twice.
        if (request.DedupeKey is not null)
        {
            var existing = await store.FindByDedupeKey(request.DedupeKey, ct);
            if (existing is not null) return Result<QueueMessageResult>.Ok(new QueueMessageResult(existing.Id, true));
        }

        var template = await store.FindTemplate(request.TemplateCode, request.Language, channel, ct);
        if (template is null)
            return Result<QueueMessageResult>.Fail("template.not_found", $"No '{request.TemplateCode}' template for {request.Channel}.");

        var body = TemplateRenderer.Render(template.Body, request.Values);
        if (!body.IsSuccess) return Result<QueueMessageResult>.Fail(body.Error!.Code, body.Error.Message);
        string? subject = null;
        if (template.Subject is not null)
        {
            var s = TemplateRenderer.Render(template.Subject, request.Values);
            if (!s.IsSuccess) return Result<QueueMessageResult>.Fail(s.Error!.Code, s.Error.Message);
            subject = s.Value;
        }

        var queued = OutboundMessage.Queue(channel, request.Destination, subject, body.Value!, request.TemplateCode, request.ShopId, request.DedupeKey, clock.GetUtcNow());
        if (!queued.IsSuccess) return Result<QueueMessageResult>.Fail(queued.Error!.Code, queued.Error.Message);

        await store.Add(queued.Value!, ct);
        await store.Save(ct);
        return Result<QueueMessageResult>.Ok(new QueueMessageResult(queued.Value!.Id, false));
    }
}

/// <summary>The worker calls this every few seconds: send what is due, retry what failed, give up after five tries.</summary>
public sealed record SendDueMessages(int Take = 50) : ICommand<SendDueResult>;

public sealed record SendDueResult(int Sent, int Retrying, int Failed);

internal sealed class SendDueMessagesHandler(
    IMessagingStore store, IEnumerable<IMessageChannelProvider> providers, IMessagePricing pricing, TimeProvider clock)
    : ICommandHandler<SendDueMessages, SendDueResult>
{
    public async Task<Result<SendDueResult>> Handle(SendDueMessages command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        int sent = 0, retrying = 0, failed = 0;

        foreach (var message in await store.Due(now, command.Take, ct))
        {
            var provider = providers.FirstOrDefault(p => p.Channel == message.Channel);
            SendOutcome outcome;
            try
            {
                outcome = provider is null
                    ? new SendOutcome(false, Permanent: true, Error: $"No provider is connected for {message.Channel}.")
                    : await provider.Send(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = new SendOutcome(false, Error: ex.Message);
            }

            if (outcome.Success)
            {
                message.MarkSent(now, pricing.CostCents(message.Channel, message.Body), outcome.ProviderRef);
                sent++;
            }
            else
            {
                message.RecordFailure(now, outcome.Error ?? "Send failed.", outcome.Permanent);
                if (message.Status == MessageStatus.Failed) failed++; else retrying++;
            }
        }

        await store.Save(ct);
        return Result<SendDueResult>.Ok(new SendDueResult(sent, retrying, failed));
    }
}

/// <summary>E3-6: a customer asked for a slip. ShopCore raised the event; this turns it into a queued message.</summary>
internal sealed class SlipRequestedHandler(IMessageQueue queue) : IDomainEventHandler<SlipRequested>
{
    public async Task Handle(SlipRequested e, CancellationToken ct)
    {
        var result = await queue.Queue(new QueueMessageRequest("slip.sale", e.Language, e.Channel, e.Destination, e.Values,
            e.ShopId, $"slip:{e.SlipRequestId:N}"), ct);

        // A message that cannot be queued (a bad number, a missing value) will not get better by waiting.
        // Throwing leaves the reason on the outbox row, where it is visible.
        if (!result.IsSuccess) throw new InvalidOperationException($"{result.Error!.Code}: {result.Error.Message}");
    }
}

internal sealed class DestinationChecker : IDestinationChecker
{
    public Result<string> Check(string channel, string? destination) =>
        MessageChannels.TryParse(channel, out var parsed)
            ? Destinations.Normalise(parsed, destination)
            : Result<string>.Fail("message.channel_invalid", "Channel must be whatsapp, sms, email or push.");
}
