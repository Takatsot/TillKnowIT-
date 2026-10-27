using TillKnowIT.SharedKernel;

namespace Marketplace.Domain;

/// <summary>M3-2: who may see a shop's low-stock signals. Off unless the owner chooses otherwise.</summary>
public enum SharingMode { Off, Usual, All }

public static class SharingModes
{
    public static bool TryParse(string? text, out SharingMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "off": mode = SharingMode.Off; return true;
            case "usual": mode = SharingMode.Usual; return true;
            case "all": mode = SharingMode.All; return true;
            default: mode = default; return false;
        }
    }
}

public sealed class SharingSetting : VersionedEntity
{
    private SharingSetting() { }

    public SharingSetting(Guid shopId, SharingMode mode)
    {
        ShopId = shopId;
        Mode = mode;
    }

    public Guid ShopId { get; private set; }
    public SharingMode Mode { get; private set; }

    public void Change(SharingMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        Touch();
    }
}

/// <summary>A supplier a shop buys from. "Usual" ones are shown first, with the price the shop last paid.</summary>
public sealed class ShopSupplier : VersionedEntity
{
    private ShopSupplier() { }

    public ShopSupplier(Guid shopId, Guid supplierId)
    {
        ShopId = shopId;
        SupplierId = supplierId;
    }

    public Guid ShopId { get; private set; }
    public Guid SupplierId { get; private set; }

    public long? LastPriceCents { get; private set; }
    public DateTimeOffset? LastOrderAt { get; private set; }

    /// <summary>Remembers what the shop paid, so next time the usual supplier is shown with its last price.</summary>
    public void RecordOrder(long? priceCents, DateTimeOffset now)
    {
        LastPriceCents = priceCents ?? LastPriceCents;
        LastOrderAt = now;
        Touch();
    }
}

/// <summary>Platform-wide marketplace settings, such as the ranking weights. Changed by a platform admin, no release needed.</summary>
public sealed class MarketplaceSetting : VersionedEntity
{
    private MarketplaceSetting() { }

    public MarketplaceSetting(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;

    public void Change(string value)
    {
        Value = value;
        Touch();
    }
}
