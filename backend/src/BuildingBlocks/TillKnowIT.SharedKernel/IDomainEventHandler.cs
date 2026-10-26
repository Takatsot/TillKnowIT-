namespace TillKnowIT.SharedKernel;

/// <summary>
/// Reacts to an event from any module. Delivery is at least once, so a handler must be safe to run twice
/// for the same <see cref="IDomainEvent.EventId"/>.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}
