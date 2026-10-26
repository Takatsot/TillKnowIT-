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

public sealed class CreditAccount : VersionedEntity
{
    private CreditAccount() { }

    public CreditAccount(Guid shopId, string name, string? phone, long? creditLimitCents)
    {
        ShopId = shopId;
        Name = name;
        Phone = phone;
        CreditLimitCents = creditLimitCents;
    }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Phone { get; private set; }
    public long? CreditLimitCents { get; private set; }
}

public enum CreditEntryType { Sale, Payment, Cancel }

/// <summary>Append only. Balance = sum of entries (sales positive, payments and cancels negative).</summary>
public sealed class CreditEntry : Entity
{
    private CreditEntry() { }

    public CreditEntry(Guid accountId, CreditEntryType type, long amountCents, Guid? saleId)
    {
        AccountId = accountId;
        Type = type;
        AmountCents = amountCents;
        SaleId = saleId;
    }

    public Guid AccountId { get; private set; }
    public CreditEntryType Type { get; private set; }
    public long AmountCents { get; private set; }
    public Guid? SaleId { get; private set; }
}

/// <summary>One per till per day.</summary>
public sealed class CashUp : Entity
{
    private CashUp() { }

    public CashUp(Guid shopId, Guid tillDeviceId, DateOnly day, long floatCents, long expectedCents, long countedCents, string? note)
    {
        ShopId = shopId;
        TillDeviceId = tillDeviceId;
        Day = day;
        FloatCents = floatCents;
        ExpectedCents = expectedCents;
        CountedCents = countedCents;
        DifferenceCents = countedCents - expectedCents;
        Note = note;
    }

    public Guid ShopId { get; private set; }
    public Guid TillDeviceId { get; private set; }
    public DateOnly Day { get; private set; }
    public long FloatCents { get; private set; }
    public long ExpectedCents { get; private set; }
    public long CountedCents { get; private set; }
    public long DifferenceCents { get; private set; }
    public string? Note { get; private set; }
}

public sealed class ComplianceDocument : VersionedEntity
{
    private ComplianceDocument() { }

    public ComplianceDocument(Guid shopId, string typeCode, string fileUrl, DateOnly? expiresOn)
    {
        ShopId = shopId;
        TypeCode = typeCode;
        FileUrl = fileUrl;
        ExpiresOn = expiresOn;
    }

    public Guid ShopId { get; private set; }
    public string TypeCode { get; private set; } = default!;
    public string FileUrl { get; private set; } = default!;
    public DateOnly? ExpiresOn { get; private set; }
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
