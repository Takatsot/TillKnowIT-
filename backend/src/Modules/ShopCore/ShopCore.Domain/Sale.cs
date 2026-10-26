using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

/// <summary>Append only. Cancellations are separate records, never edits.</summary>
public sealed class Sale : Entity
{
    private Sale() { }

    public Sale(Guid shopId, Guid tillDeviceId, Guid staffId, string paymentMethodCode,
        long vatCents, long? tenderedCents, Guid? accountId, IEnumerable<SaleLine> lines)
    {
        ShopId = shopId;
        TillDeviceId = tillDeviceId;
        StaffId = staffId;
        PaymentMethodCode = paymentMethodCode;
        VatCents = vatCents;
        TenderedCents = tenderedCents;
        AccountId = accountId;
        Lines = lines.ToList();
        TotalCents = Lines.Sum(l => l.PriceCents * l.Quantity);
        ChangeCents = tenderedCents is { } t ? t - TotalCents : null;
        SoldAt = DateTimeOffset.UtcNow;
    }

    public Guid ShopId { get; private set; }
    public Guid TillDeviceId { get; private set; }
    public Guid StaffId { get; private set; }
    public long TotalCents { get; private set; }
    public long VatCents { get; private set; }
    public string PaymentMethodCode { get; private set; } = default!;
    public long? TenderedCents { get; private set; }
    public long? ChangeCents { get; private set; }
    public Guid? AccountId { get; private set; }
    public DateTimeOffset SoldAt { get; private set; }
    public List<SaleLine> Lines { get; private set; } = [];
}

/// <summary>Name and prices are copied at the time of sale.</summary>
public sealed class SaleLine
{
    private SaleLine() { }

    public SaleLine(Guid productId, string name, int quantity, long priceCents, long costCents)
    {
        ProductId = productId;
        Name = name;
        Quantity = quantity;
        PriceCents = priceCents;
        CostCents = costCents;
    }

    public Guid SaleId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Name { get; private set; } = default!;
    public int Quantity { get; private set; }
    public long PriceCents { get; private set; }
    public long CostCents { get; private set; }
}
