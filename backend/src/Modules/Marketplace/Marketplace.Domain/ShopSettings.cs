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

/// <summary>A supplier a shop buys from. "Usual" ones are shown first. Added by the owner, or automatically once an order is received.</summary>
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
    public DateTimeOffset? LastOrderAt { get; private set; }

    public void RecordOrder(DateTimeOffset now)
    {
        LastOrderAt = now;
        Touch();
    }
}

/// <summary>What a shop last paid a supplier for a product (by barcode), per case, so the usual supplier shows its last price.</summary>
public sealed class LastPrice : VersionedEntity
{
    private LastPrice() { }

    public LastPrice(Guid shopId, Guid supplierId, string barcode, long casePriceCents, DateTimeOffset now)
    {
        ShopId = shopId;
        SupplierId = supplierId;
        Barcode = barcode;
        CasePriceCents = casePriceCents;
        PaidAt = now;
    }

    public Guid ShopId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string Barcode { get; private set; } = default!;
    public long CasePriceCents { get; private set; }
    public DateTimeOffset PaidAt { get; private set; }

    public void Update(long casePriceCents, DateTimeOffset now)
    {
        CasePriceCents = casePriceCents;
        PaidAt = now;
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
