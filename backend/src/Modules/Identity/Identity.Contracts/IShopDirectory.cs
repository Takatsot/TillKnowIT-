namespace Identity.Contracts;

/// <summary>Who runs a shop and how to reach them. The owner's phone and email come from their person record.</summary>
public sealed record ShopInfo(Guid ShopId, string Name, string Status, Guid? OwnerPersonId, string? OwnerPhone, string? OwnerEmail, string OwnerLanguage);

/// <summary>Lets other modules ask about shops without reading Identity's tables.</summary>
public interface IShopDirectory
{
    Task<ShopInfo?> Get(Guid shopId, CancellationToken ct);

    /// <summary>Every shop that a field agent has verified. Summaries and alerts are only sent for these.</summary>
    Task<List<Guid>> VerifiedShopIds(CancellationToken ct);
}

public sealed record StaleDevice(Guid ShopId, Guid DeviceId, string Name, DateTimeOffset LastSeenAt);

/// <summary>Approved tills that have stopped syncing. The owner is alerted at 48 hours; the till shows red from 24.</summary>
public interface IDeviceDirectory
{
    Task<List<StaleDevice>> NotSeenSince(DateTimeOffset cutoff, CancellationToken ct);
}
