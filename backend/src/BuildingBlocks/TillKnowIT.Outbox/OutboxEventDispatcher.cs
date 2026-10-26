using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.SharedKernel;

namespace TillKnowIT.Outbox;

public static class OutboxEventDispatcher
{
    /// <summary>Rebuilds the event from an outbox row and runs every registered handler for it, in any module.</summary>
    public static async Task Dispatch(OutboxMessage message, IServiceProvider services, CancellationToken ct)
    {
        var eventType = Type.GetType(message.Type)
                        ?? throw new InvalidOperationException($"Event type '{message.Type}' is not loaded.");
        var domainEvent = JsonSerializer.Deserialize(message.Payload, eventType)
                          ?? throw new InvalidOperationException($"Event payload for '{eventType.Name}' is empty.");

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handle = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!;
        foreach (var handler in services.GetServices(handlerType))
            await (Task)handle.Invoke(handler, [domainEvent, ct])!;
    }
}
