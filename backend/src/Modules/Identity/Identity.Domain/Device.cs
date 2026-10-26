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

    /// <summary>The owner approves a pending device. A switched-off device must be registered again.</summary>
    public Result Approve()
    {
        if (Status == DeviceStatus.Disabled)
            return Result.Fail("device.disabled", "This device was switched off. Register it again.");
        Status = DeviceStatus.Approved;
        Touch();
        return Result.Ok();
    }

    /// <summary>Switch the device off. The till clears its local data the next time it starts and checks in.</summary>
    public void Wipe()
    {
        if (Status == DeviceStatus.Disabled && WipeRequested) return;
        Status = DeviceStatus.Disabled;
        WipeRequested = true;
        Touch();
    }

    public void ConfirmWiped()
    {
        if (!WipeRequested) return;
        WipeRequested = false;
        Touch();
    }

    public void RecordSeen(DateTimeOffset now)
    {
        LastSeenAt = now;
        Touch();
    }
}
