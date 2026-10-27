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
