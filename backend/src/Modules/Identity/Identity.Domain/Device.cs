using TillKnowIT.SharedKernel;

namespace Identity.Domain;

public enum DeviceStatus { Pending, Approved, Disabled }

public sealed class Device : VersionedEntity
{
    private Device() { }

    public Device(Guid shopId, string name)
    {
        ShopId = shopId;
        Name = name;
    }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = default!;
    public DeviceStatus Status { get; private set; } = DeviceStatus.Pending;
    public DateTimeOffset? LastSeenAt { get; private set; }
    public bool WipeRequested { get; private set; }

    public void Approve()
    {
        Status = DeviceStatus.Approved;
        Touch();
    }

    public void Wipe()
    {
        Status = DeviceStatus.Disabled;
        WipeRequested = true;
        Touch();
    }
}
