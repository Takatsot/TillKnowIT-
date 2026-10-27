using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public static class PaymentMethods
{
    public const string Cash = "cash";
    public const string Card = "card";
    public const string Qr = "qr";
    public const string Credit = "credit";

    /// <summary>The sale was paid in more than one way; the parts are on the sale.</summary>
    public const string Split = "split";

    /// <summary>The goods were paid for through a lay-by plan, over time, before they were collected.</summary>
    public const string Layby = "layby";

    // Matches the seeded payment_methods lookup.
    public static bool IsValid(string? code) => code is Cash or Card or Qr or Credit or Split or Layby;
}

/// <summary>ExpiryOverrideBy is the manager who approved selling an expired item, with their PIN. Null for ordinary lines.</summary>
/// <summary>
/// UnitDivisor is how many of the quantity make one priced unit: 1 for things sold each, 1000 for things sold by weight (quantity in grams,
/// price per kilogram). Nothing here knows which shops sell by weight; it only knows the arithmetic.
/// </summary>
/// <summary>
/// DiscountCents is taken off the whole line (not per unit, so a combo or a percentage never needs to divide evenly). A discount says why:
/// a promotion the shop runs, or a manager who approved the price (PriceOverrideBy), which is checked against that manager's role.
/// </summary>
public sealed record SaleLineInput(
    Guid ProductId, string Name, int Quantity, long PriceCents, long CostCents, Guid? ExpiryOverrideBy = null, int UnitDivisor = 1,
    long DiscountCents = 0, Guid? PromotionId = null, Guid? PriceOverrideBy = null);

public static class SaleMath
{
    public static bool IsValidDivisor(int divisor) => divisor is 1 or 1000;

    /// <summary>Price times quantity over the divisor, rounded half up to the cent, before any discount.</summary>
    public static long LineGross(long priceCents, int quantity, int divisor = 1) => divisor <= 1 ? priceCents * quantity : (priceCents * quantity + divisor / 2) / divisor;

    /// <summary>What the line costs the customer: the gross less its discount.</summary>
    public static long LineTotal(long priceCents, int quantity, int divisor = 1, long discountCents = 0) => LineGross(priceCents, quantity, divisor) - discountCents;
}

public sealed record SaleInput(
    Guid Id, Guid ShopId, Guid TillDeviceId, Guid StaffId, DateTimeOffset SoldAt,
    string PaymentMethodCode, long TotalCents, long VatCents, long? TenderedCents, long? ChangeCents,
    Guid? AccountId, IReadOnlyList<SaleLineInput> Lines,
    IReadOnlyList<Selling.PaymentPart>? Payments = null, long TipCents = 0, Guid? CustomerId = null);

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
            if (!SaleMath.IsValidDivisor(line.UnitDivisor))
                return Result<Sale>.Fail("sale.line_unit", "The unit of an item is each or by weight.");
            if (line.DiscountCents < 0 || line.DiscountCents > SaleMath.LineGross(line.PriceCents, line.Quantity, line.UnitDivisor))
                return Result<Sale>.Fail("sale.discount_invalid", "A discount cannot be negative or more than the item costs.");
            if (line.DiscountCents > 0 && line.PromotionId is null && line.PriceOverrideBy is null)
                return Result<Sale>.Fail("sale.discount_unexplained", "A discount needs a promotion or a manager's approval.");
        }
        if (input.Lines.Select(l => l.ProductId).Distinct().Count() != input.Lines.Count)
            return Result<Sale>.Fail("sale.duplicate_line", "Each product may appear once; add the quantities together.");
        if (!PaymentMethods.IsValid(input.PaymentMethodCode))
            return Result<Sale>.Fail("sale.payment_method", "Unknown payment method.");
        if (input.SoldAt > now + MaxClockSkew)
            return Result<Sale>.Fail("sale.date_invalid", "The sale is dated in the future.");

        var total = input.Lines.Sum(l => SaleMath.LineTotal(l.PriceCents, l.Quantity, l.UnitDivisor, l.DiscountCents));
        if (input.TotalCents != total)
            return Result<Sale>.Fail("sale.total_mismatch", "The total does not match the items.");
        if (input.VatCents < 0 || input.VatCents > total)
            return Result<Sale>.Fail("sale.vat_invalid", "VAT is not valid for this total.");
        if (input.TipCents < 0 || input.TipCents > total)
            return Result<Sale>.Fail("sale.tip_invalid", "A tip cannot be negative or more than the bill.");

        // What the customer hands over: the items plus any tip.
        var owed = total + input.TipCents;
        long? tendered = input.TenderedCents;
        long? change = input.ChangeCents;
        IReadOnlyList<Selling.PaymentPart> parts = [];

        if (input.PaymentMethodCode == PaymentMethods.Split)
        {
            if (input.Payments is not { Count: > 0 })
                return Result<Sale>.Fail("sale.payment_parts_required", "A split sale needs its payments.");
            // A credit sale is on one account for the whole sale, so credit cannot be one part of several.
            if (input.Payments.Any(p => p.MethodCode == PaymentMethods.Credit))
                return Result<Sale>.Fail("sale.split_credit", "Credit cannot be part of a split payment.");
            var checkedParts = Selling.SplitPayments.Check(input.Payments, owed);
            if (!checkedParts.IsSuccess) return Result<Sale>.Fail(checkedParts.Error!.Code, checkedParts.Error.Message);
            if (input.TenderedCents is not null || input.ChangeCents is not null)
                return Result<Sale>.Fail("sale.tender_invalid", "The cash of a split sale is on its cash part.");
            parts = input.Payments;
            var cash = parts.FirstOrDefault(p => p.MethodCode == PaymentMethods.Cash);
            tendered = cash?.TenderedCents;
            change = cash?.TenderedCents is { } t ? t - cash.AmountCents : null;
        }
        else
        {
            if (input.Payments is { Count: > 0 })
                return Result<Sale>.Fail("sale.payment_method", "A sale paid in parts is a split sale.");
            if (input.PaymentMethodCode == PaymentMethods.Cash)
            {
                if (input.TenderedCents is null || input.TenderedCents < owed || input.ChangeCents != input.TenderedCents - owed)
                    return Result<Sale>.Fail("sale.tender_invalid", "Cash tendered and change do not add up.");
            }
            else if (input.TenderedCents is not null || input.ChangeCents is not null)
            {
                return Result<Sale>.Fail("sale.tender_invalid", "Only cash sales have tendered and change.");
            }
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
            TenderedCents = tendered,
            ChangeCents = change,
            AccountId = input.AccountId,
            TipCents = input.TipCents,
            CustomerId = input.CustomerId,
            Payments = parts.Select(p => new SalePayment(p.MethodCode, p.AmountCents, p.Reference, p.TenderedCents)).ToList(),
            SoldAt = input.SoldAt,
            Lines = input.Lines
                .Select(l => new SaleLine(l.ProductId, l.Name.Trim(), l.Quantity, l.PriceCents, l.CostCents, l.ExpiryOverrideBy, l.UnitDivisor, l.DiscountCents, l.PromotionId, l.PriceOverrideBy))
                .ToList(),
        };
        sale.StampFromDevice(input.Id, input.SoldAt, input.StaffId, input.TillDeviceId);
        foreach (var line in sale.Lines) line.AttachTo(sale.Id);
        foreach (var payment in sale.Payments) payment.AttachTo(sale.Id);
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

    /// <summary>What the customer gave the staff on top of the bill. Not part of the sale total or of sales figures; it is in the cash drawer.</summary>
    public long TipCents { get; private set; }

    /// <summary>The customer the sale was attached to, for loyalty. Optional.</summary>
    public Guid? CustomerId { get; private set; }

    public List<SaleLine> Lines { get; private set; } = [];

    /// <summary>Only for split sales: how each part was paid.</summary>
    public List<SalePayment> Payments { get; private set; } = [];

    /// <summary>The cash this sale put in the drawer: the whole bill and tip for a cash sale, the cash part of a split sale, nothing otherwise.</summary>
    public long CashCents => PaymentMethodCode == PaymentMethods.Cash
        ? TotalCents + TipCents
        : Payments.Where(p => p.MethodCode == PaymentMethods.Cash).Sum(p => p.AmountCents);
}

/// <summary>One part of a split sale.</summary>
public sealed class SalePayment
{
    private SalePayment() { }

    public SalePayment(string methodCode, long amountCents, string? reference, long? tenderedCents)
    {
        MethodCode = methodCode;
        AmountCents = amountCents;
        Reference = reference;
        TenderedCents = tenderedCents;
    }

    internal void AttachTo(Guid saleId) => SaleId = saleId;

    public Guid SaleId { get; private set; }
    public string MethodCode { get; private set; } = default!;
    public long AmountCents { get; private set; }
    public string? Reference { get; private set; }
    public long? TenderedCents { get; private set; }
}

/// <summary>Name and prices are copied at the time of sale.</summary>
public sealed class SaleLine
{
    private SaleLine() { }

    public SaleLine(Guid productId, string name, int quantity, long priceCents, long costCents, Guid? expiryOverrideBy = null, int unitDivisor = 1,
        long discountCents = 0, Guid? promotionId = null, Guid? priceOverrideBy = null)
    {
        DiscountCents = discountCents;
        PromotionId = promotionId;
        PriceOverrideBy = priceOverrideBy;
        UnitDivisor = unitDivisor;
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

    /// <summary>1 for items sold each; 1000 for items sold by weight, where quantity is grams and the price is per kilogram.</summary>
    public int UnitDivisor { get; private set; } = 1;

    /// <summary>Taken off the whole line.</summary>
    public long DiscountCents { get; private set; }

    /// <summary>The promotion that gave the discount, if it was one.</summary>
    public Guid? PromotionId { get; private set; }

    /// <summary>The manager who approved the price, if the discount was an override.</summary>
    public Guid? PriceOverrideBy { get; private set; }

    /// <summary>What this line cost the customer, after its discount.</summary>
    public long TotalCents => SaleMath.LineTotal(PriceCents, Quantity, UnitDivisor, DiscountCents);

    /// <summary>Who approved selling this item although it had expired. Kept for the owner to review.</summary>
    public Guid? ExpiryOverrideBy { get; private set; }
}
