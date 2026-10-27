using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public static class PaymentMethods
{
    public const string Cash = "cash";
    public const string Card = "card";
    public const string Qr = "qr";
    public const string Credit = "credit";

    // Matches the seeded payment_methods lookup.
    public static bool IsValid(string? code) => code is Cash or Card or Qr or Credit;
}

/// <summary>ExpiryOverrideBy is the manager who approved selling an expired item, with their PIN. Null for ordinary lines.</summary>
public sealed record SaleLineInput(Guid ProductId, string Name, int Quantity, long PriceCents, long CostCents, Guid? ExpiryOverrideBy = null);

public sealed record SaleInput(
    Guid Id, Guid ShopId, Guid TillDeviceId, Guid StaffId, DateTimeOffset SoldAt,
    string PaymentMethodCode, long TotalCents, long VatCents, long? TenderedCents, long? ChangeCents,
    Guid? AccountId, IReadOnlyList<SaleLineInput> Lines);

/// <summary>Append only. Cancellations are separate records, never edits.</summary>
public sealed class Sale : Entity
{
    /// <summary>Tills may be a little out; anything further ahead than this is rejected.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromHours(24);

    private Sale() { }

    /// <summary>Checks a sale that a till uploaded. Totals are recomputed here; the till is not trusted.</summary>
    public static Result<Sale> Record(SaleInput input, DateTimeOffset now)
    {
        if (input.Lines.Count == 0)
            return Result<Sale>.Fail("sale.no_lines", "A sale needs at least one item.");
        foreach (var line in input.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Name))
                return Result<Sale>.Fail("sale.line_name", "Every item needs a name.");
            if (line.Quantity <= 0)
                return Result<Sale>.Fail("sale.line_quantity", "Item quantity must be more than zero.");
            if (line.PriceCents < 0 || line.CostCents < 0)
                return Result<Sale>.Fail("sale.line_price", "Prices cannot be negative.");
        }
        if (input.Lines.Select(l => l.ProductId).Distinct().Count() != input.Lines.Count)
            return Result<Sale>.Fail("sale.duplicate_line", "Each product may appear once; add the quantities together.");
        if (!PaymentMethods.IsValid(input.PaymentMethodCode))
            return Result<Sale>.Fail("sale.payment_method", "Unknown payment method.");
        if (input.SoldAt > now + MaxClockSkew)
            return Result<Sale>.Fail("sale.date_invalid", "The sale is dated in the future.");

        var total = input.Lines.Sum(l => l.PriceCents * l.Quantity);
        if (input.TotalCents != total)
            return Result<Sale>.Fail("sale.total_mismatch", "The total does not match the items.");
        if (input.VatCents < 0 || input.VatCents > total)
            return Result<Sale>.Fail("sale.vat_invalid", "VAT is not valid for this total.");

        if (input.PaymentMethodCode == PaymentMethods.Cash)
        {
            if (input.TenderedCents is null || input.TenderedCents < total || input.ChangeCents != input.TenderedCents - total)
                return Result<Sale>.Fail("sale.tender_invalid", "Cash tendered and change do not add up.");
        }
        else if (input.TenderedCents is not null || input.ChangeCents is not null)
        {
            return Result<Sale>.Fail("sale.tender_invalid", "Only cash sales have tendered and change.");
        }

        if (input.PaymentMethodCode == PaymentMethods.Credit && input.AccountId is null)
            return Result<Sale>.Fail("sale.account_required", "A credit sale needs a credit account.");
        if (input.PaymentMethodCode != PaymentMethods.Credit && input.AccountId is not null)
            return Result<Sale>.Fail("sale.account_unexpected", "Only credit sales have a credit account.");

        var sale = new Sale
        {
            ShopId = input.ShopId,
            TillDeviceId = input.TillDeviceId,
            StaffId = input.StaffId,
            TotalCents = total,
            VatCents = input.VatCents,
            PaymentMethodCode = input.PaymentMethodCode,
            TenderedCents = input.TenderedCents,
            ChangeCents = input.ChangeCents,
            AccountId = input.AccountId,
            SoldAt = input.SoldAt,
            Lines = input.Lines
                .Select(l => new SaleLine(l.ProductId, l.Name.Trim(), l.Quantity, l.PriceCents, l.CostCents, l.ExpiryOverrideBy))
                .ToList(),
        };
        sale.StampFromDevice(input.Id, input.SoldAt, input.StaffId, input.TillDeviceId);
        foreach (var line in sale.Lines) line.AttachTo(sale.Id);
        return Result<Sale>.Ok(sale);
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

    public SaleLine(Guid productId, string name, int quantity, long priceCents, long costCents, Guid? expiryOverrideBy = null)
    {
        ExpiryOverrideBy = expiryOverrideBy;
        ProductId = productId;
        Name = name;
        Quantity = quantity;
        PriceCents = priceCents;
        CostCents = costCents;
    }

    internal void AttachTo(Guid saleId) => SaleId = saleId;

    public Guid SaleId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Name { get; private set; } = default!;
    public int Quantity { get; private set; }
    public long PriceCents { get; private set; }
    public long CostCents { get; private set; }

    /// <summary>Who approved selling this item although it had expired. Kept for the owner to review.</summary>
    public Guid? ExpiryOverrideBy { get; private set; }
}
