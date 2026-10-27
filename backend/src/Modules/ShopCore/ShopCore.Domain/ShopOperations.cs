using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public sealed class SaleCancellation : Entity
{
    private SaleCancellation() { }

    public static Result<SaleCancellation> Record(Guid id, Guid saleId, Guid shopId, Guid staffId, Guid deviceId, string? reason, DateTimeOffset cancelledAt)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result<SaleCancellation>.Fail("cancellation.reason_required", "A cancellation needs a reason.");

        var c = new SaleCancellation { SaleId = saleId, ShopId = shopId, StaffId = staffId, Reason = reason.Trim(), CancelledAt = cancelledAt };
        c.StampFromDevice(id, cancelledAt, staffId, deviceId);
        return Result<SaleCancellation>.Ok(c);
    }

    public Guid SaleId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid StaffId { get; private set; }
    public string Reason { get; private set; } = default!;
    public DateTimeOffset CancelledAt { get; private set; }
}

public sealed class ShopSetting : VersionedEntity
{
    private ShopSetting() { }

    public ShopSetting(Guid shopId, string key, string value)
    {
        ShopId = shopId;
        Key = key;
        Value = value;
    }

    public Guid ShopId { get; private set; }
    public string Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;
}

public enum VasProductType { Airtime, Data, Electricity }

/// <summary>Online only.</summary>
public sealed class VasSale : Entity
{
    private VasSale() { }

    public VasSale(Guid shopId, VasProductType type, long amountCents, long commissionCents, string? partnerRef)
    {
        ShopId = shopId;
        ProductType = type;
        AmountCents = amountCents;
        CommissionCents = commissionCents;
        PartnerRef = partnerRef;
    }

    public Guid ShopId { get; private set; }
    public VasProductType ProductType { get; private set; }
    public long AmountCents { get; private set; }
    public long CommissionCents { get; private set; }
    public string? PartnerRef { get; private set; }
    public string Status { get; private set; } = "pending";
}
