namespace TillKnowIT.SharedKernel;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

/// <summary>Base for every table: UUID v7 id plus created_at, created_by, device_id.</summary>
public abstract class Entity : IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; protected set; }
    public Guid? DeviceId { get; protected set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;
    public void ClearDomainEvents() => _events.Clear();
    protected void Raise(IDomainEvent e) => _events.Add(e);
}

/// <summary>Editable tables also carry updated_at and a version for last-change-wins sync.</summary>
public abstract class VersionedEntity : Entity
{
    public DateTimeOffset? UpdatedAt { get; protected set; }
    public int Version { get; protected set; } = 1;

    protected void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
    }
}
