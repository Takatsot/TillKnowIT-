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

public enum SlipChannel { Qr, Whatsapp, Sms, Email }

public sealed class SlipRequest : Entity
{
    private SlipRequest() { }

    public SlipRequest(Guid saleId, SlipChannel channel, string? destination)
    {
        SaleId = saleId;
        Channel = channel;
        Destination = destination;
    }

    public Guid SaleId { get; private set; }
    public SlipChannel Channel { get; private set; }
    public string? Destination { get; private set; }
    public string Status { get; private set; } = "queued";
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

public enum ImportStatus { Queued, Reading, Ready, Confirmed, Failed }

public sealed class InvoiceImport : VersionedEntity
{
    private InvoiceImport() { }

    public InvoiceImport(Guid shopId, string fileUrl)
    {
        ShopId = shopId;
        FileUrl = fileUrl;
    }

    public Guid ShopId { get; private set; }
    public string FileUrl { get; private set; } = default!;
    public ImportStatus Status { get; private set; } = ImportStatus.Queued;
    public string? ResultJson { get; private set; }

    public void Complete(string json)
    {
        ResultJson = json;
        Status = ImportStatus.Ready;
        Touch();
    }
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
