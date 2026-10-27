using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TillKnowIT.SharedKernel;

namespace ShopCore.Domain.Selling;

public enum QuoteStatus { Open, Converted, Expired, Cancelled }

/// <summary>
/// B6-1: a price promised to a customer until a date. It can become a sale (it is turned into an open ticket with the same lines) or lapse. The total is
/// worked out here from the lines, never taken from the till.
/// </summary>
public sealed class Quote : VersionedEntity
{
    public const int MaxValidDays = 90;

    private Quote() { }

    public Guid ShopId { get; private set; }
    public string Number { get; private set; } = default!;
    public string? CustomerName { get; private set; }
    public string? CustomerPhone { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string LinesJson { get; private set; } = "[]";
    public long TotalCents { get; private set; }
    public DateOnly ExpiresOn { get; private set; }
    public QuoteStatus Status { get; private set; }
    public Guid? TicketId { get; private set; }

    public IReadOnlyList<TicketLine> Lines => JsonSerializer.Deserialize<List<TicketLine>>(LinesJson) ?? [];

    public static Result<Quote> Create(Guid id, Guid shopId, Guid staffId, Guid deviceId, string number, string? customerName, string? customerPhone, Guid? customerId,
        IReadOnlyList<TicketLine> lines, DateOnly expiresOn, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Trim().Length > 20) return Result<Quote>.Fail("quote.number_invalid", "A quote needs a number of up to 20 characters.");
        if (lines.Count is 0 or > OpenTicket.MaxLines) return Result<Quote>.Fail("quote.lines_invalid", "A quote needs from 1 to 100 items.");
        if (lines.Any(l => string.IsNullOrWhiteSpace(l.Name) || l.Quantity <= 0 || l.PriceCents < 0 || !SaleMath.IsValidDivisor(l.UnitDivisor)))
            return Result<Quote>.Fail("quote.lines_invalid", "Every item needs a name, a quantity and a price.");
        var today = BusinessDay.Of(now);
        if (expiresOn < today || expiresOn.DayNumber - today.DayNumber > MaxValidDays) return Result<Quote>.Fail("quote.expiry_invalid", $"A quote is valid from today for up to {MaxValidDays} days.");
        if (customerName is { Length: > 200 } || customerPhone is { Length: > 20 }) return Result<Quote>.Fail("quote.customer_invalid", "The customer details are too long.");

        var quote = new Quote
        {
            ShopId = shopId, Number = number.Trim(), CustomerName = Clean(customerName), CustomerPhone = Clean(customerPhone), CustomerId = customerId,
            LinesJson = JsonSerializer.Serialize(lines), TotalCents = lines.Sum(l => l.TotalCents), ExpiresOn = expiresOn, Status = QuoteStatus.Open,
        };
        quote.StampFromDevice(id, now, staffId, deviceId);
        return Result<Quote>.Ok(quote);
    }

    /// <summary>An open quote past its date is expired, whether or not anyone has marked it.</summary>
    public QuoteStatus StatusOn(DateOnly today) => Status == QuoteStatus.Open && today > ExpiresOn ? QuoteStatus.Expired : Status;

    public Result Convert(Guid ticketId, DateOnly today)
    {
        var status = StatusOn(today);
        if (status != QuoteStatus.Open) return Result.Fail(status == QuoteStatus.Expired ? "quote.expired" : "quote.not_open", status == QuoteStatus.Expired ? "This quote has expired." : "This quote is no longer open.");
        Status = QuoteStatus.Converted;
        TicketId = ticketId;
        Touch();
        return Result.Ok();
    }

    public Result Cancel()
    {
        if (Status != QuoteStatus.Open) return Result.Fail("quote.not_open", "This quote is no longer open.");
        Status = QuoteStatus.Cancelled;
        Touch();
        return Result.Ok();
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

public enum LaybyStatus { Active, Completed, Cancelled, Expired, Collected }

/// <summary>The shop's lay-by terms, from the pack settings; each has a sensible default.</summary>
public sealed record LaybyTerms(int MinDepositPercent = 20, int MaxWeeks = 12, int CancelFeePercent = 10, long MinCancelFeeCents = 2_000)
{
    public static LaybyTerms FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new LaybyTerms();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            int Int(string name, int fallback, int min, int max) => r.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) && n >= min && n <= max ? n : fallback;
            long Long(string name, long fallback) => r.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) && n >= 0 ? n : fallback;
            return new LaybyTerms(Int("minDepositPercent", 20, 0, 100), Int("maxWeeks", 12, 1, 52), Int("cancelFeePercent", 10, 0, 100), Long("minCancelFeeCents", 2_000));
        }
        catch (JsonException) { return new LaybyTerms(); }
    }
}

/// <summary>
/// B6-2: goods set aside and paid for over time. The stock is reserved while the plan is active (a query reads the reservations; nothing is deducted until
/// the goods are collected, as an ordinary sale). Payments are appended; the balance is the total less what was paid. Cancelling, or letting it expire,
/// keeps a fee from what was paid and gives the rest back.
/// </summary>
public sealed class LaybyPlan : VersionedEntity
{
    private readonly List<LaybyPayment> _payments = [];

    private LaybyPlan() { }

    public Guid ShopId { get; private set; }
    public string Reference { get; private set; } = default!;
    public string CustomerName { get; private set; } = default!;
    public string? CustomerPhone { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string LinesJson { get; private set; } = "[]";
    public long TotalCents { get; private set; }
    public DateOnly DueOn { get; private set; }
    public LaybyStatus Status { get; private set; }
    public long CancellationFeeCents { get; private set; }
    public long RefundDueCents { get; private set; }
    public Guid? SaleId { get; private set; }
    public IReadOnlyList<LaybyPayment> Payments => _payments;

    public IReadOnlyList<TicketLine> Lines => JsonSerializer.Deserialize<List<TicketLine>>(LinesJson) ?? [];
    public long PaidCents => _payments.Sum(p => p.AmountCents);
    public long BalanceCents => TotalCents - PaidCents;

    public static Result<LaybyPlan> Create(Guid id, Guid shopId, Guid staffId, Guid deviceId, string reference, string? customerName, string? customerPhone, Guid? customerId,
        IReadOnlyList<TicketLine> lines, long depositCents, string depositMethod, DateOnly dueOn, LaybyTerms terms, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 20) return Result<LaybyPlan>.Fail("layby.reference_invalid", "A lay-by needs a reference of up to 20 characters.");
        if (string.IsNullOrWhiteSpace(customerName) || customerName.Trim().Length > 200) return Result<LaybyPlan>.Fail("layby.customer_required", "Say whose lay-by it is.");
        if (lines.Count is 0 or > OpenTicket.MaxLines || lines.Any(l => string.IsNullOrWhiteSpace(l.Name) || l.Quantity <= 0 || l.PriceCents < 0 || !SaleMath.IsValidDivisor(l.UnitDivisor)))
            return Result<LaybyPlan>.Fail("layby.lines_invalid", "A lay-by needs items with a name, a quantity and a price.");
        var total = lines.Sum(l => l.TotalCents);
        if (total <= 0) return Result<LaybyPlan>.Fail("layby.total_invalid", "There is nothing to pay.");

        var minimum = (total * terms.MinDepositPercent + 99) / 100;   // rounded up, so the deposit is never under the percentage
        if (depositCents < minimum || depositCents <= 0) return Result<LaybyPlan>.Fail("layby.deposit_too_small", $"The deposit must be at least {terms.MinDepositPercent}% ({minimum} cents).");
        if (depositCents >= total) return Result<LaybyPlan>.Fail("layby.deposit_too_large", "A deposit that covers everything is just a sale.");
        var today = BusinessDay.Of(now);
        if (dueOn <= today || dueOn.DayNumber - today.DayNumber > terms.MaxWeeks * 7) return Result<LaybyPlan>.Fail("layby.due_invalid", $"The lay-by must be paid off within {terms.MaxWeeks} weeks.");
        if (depositMethod is not (PaymentMethods.Cash or PaymentMethods.Card or PaymentMethods.Qr)) return Result<LaybyPlan>.Fail("layby.method_invalid", "Pay the deposit with cash, card or QR.");

        var plan = new LaybyPlan
        {
            ShopId = shopId, Reference = reference.Trim(), CustomerName = customerName.Trim(), CustomerPhone = string.IsNullOrWhiteSpace(customerPhone) ? null : customerPhone.Trim(), CustomerId = customerId,
            LinesJson = JsonSerializer.Serialize(lines), TotalCents = total, DueOn = dueOn, Status = LaybyStatus.Active,
        };
        plan.StampFromDevice(id, now, staffId, deviceId);
        plan._payments.Add(new LaybyPayment(plan.Id, depositCents, depositMethod, staffId, deviceId, now));
        return Result<LaybyPlan>.Ok(plan);
    }

    /// <summary>A payment towards the balance. It can never be more than is owing; paying it off completes the plan.</summary>
    public Result Pay(long amountCents, string method, Guid staffId, Guid deviceId, DateTimeOffset now)
    {
        if (Status != LaybyStatus.Active) return Result.Fail("layby.not_active", "This lay-by is not active.");
        if (amountCents <= 0) return Result.Fail("layby.amount_invalid", "The payment must be more than zero.");
        if (amountCents > BalanceCents) return Result.Fail("layby.overpaid", $"Only {BalanceCents} cents is owing.");
        if (method is not (PaymentMethods.Cash or PaymentMethods.Card or PaymentMethods.Qr)) return Result.Fail("layby.method_invalid", "Pay with cash, card or QR.");
        _payments.Add(new LaybyPayment(Id, amountCents, method, staffId, deviceId, now));
        if (BalanceCents == 0) Status = LaybyStatus.Completed;
        Touch();
        return Result.Ok();
    }

    /// <summary>The fee kept when a lay-by is cancelled or expires: the shop's percentage of the total, at least the minimum, never more than was paid.</summary>
    public long FeeFor(LaybyTerms terms) => Math.Min(PaidCents, Math.Max(terms.MinCancelFeeCents, (TotalCents * terms.CancelFeePercent + 50) / 100));

    public Result Cancel(LaybyTerms terms)
    {
        if (Status != LaybyStatus.Active) return Result.Fail("layby.not_active", "Only an active lay-by can be cancelled.");
        Settle(LaybyStatus.Cancelled, terms);
        return Result.Ok();
    }

    /// <summary>A plan past its due date that is not paid off lapses on the same terms as a cancellation.</summary>
    public bool ExpireIfDue(DateOnly today, LaybyTerms terms)
    {
        if (Status != LaybyStatus.Active || today <= DueOn) return false;
        Settle(LaybyStatus.Expired, terms);
        return true;
    }

    /// <summary>The paid-off goods were handed over and rung up as an ordinary sale.</summary>
    public Result Collect(Guid saleId)
    {
        if (Status != LaybyStatus.Completed) return Result.Fail("layby.not_complete", "The lay-by is not paid off yet.");
        Status = LaybyStatus.Collected;
        SaleId = saleId;
        Touch();
        return Result.Ok();
    }

    /// <summary>Items set aside by this plan, while it is active or waiting to be collected.</summary>
    public bool ReservesStock => Status is LaybyStatus.Active or LaybyStatus.Completed;

    private void Settle(LaybyStatus status, LaybyTerms terms)
    {
        CancellationFeeCents = FeeFor(terms);
        RefundDueCents = PaidCents - CancellationFeeCents;
        Status = status;
        Touch();
    }
}

public sealed class LaybyPayment : Entity
{
    private LaybyPayment() { }

    public LaybyPayment(Guid planId, long amountCents, string methodCode, Guid staffId, Guid deviceId, DateTimeOffset paidAt)
    {
        PlanId = planId;
        AmountCents = amountCents;
        MethodCode = methodCode;
        PaidAt = paidAt;
        CreatedBy = staffId;
        DeviceId = deviceId;
        CreatedAt = paidAt;
    }

    public Guid PlanId { get; private set; }
    public long AmountCents { get; private set; }
    public string MethodCode { get; private set; } = default!;
    public DateTimeOffset PaidAt { get; private set; }
}

public enum VoucherEntryType { Issue, Redeem, Refund, Adjust }

/// <summary>
/// B7-3: a gift voucher. The code is a bearer instrument, so only its hash is kept (the customer holds the code). The balance is never stored: it is the
/// sum of the voucher's entries, so it is always right and always explainable.
/// </summary>
public sealed class GiftVoucher : VersionedEntity
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
    public const long MaxCents = 500_000;
    public const int DefaultValidMonths = 12;

    private readonly List<VoucherEntry> _entries = [];

    private GiftVoucher() { }

    public Guid ShopId { get; private set; }
    public string CodeHash { get; private set; } = default!;
    public string CodeLast4 { get; private set; } = default!;
    public DateOnly? ExpiresOn { get; private set; }
    public bool IsVoid { get; private set; }

    /// <summary>How the customer paid for it (cash, card or QR), or null when it was issued as a refund. Cash sales of vouchers count in the till.</summary>
    public string? PaidWithMethod { get; private set; }

    public long InitialCents { get; private set; }
    public IReadOnlyList<VoucherEntry> Entries => _entries;
    public long BalanceCents => _entries.Sum(e => e.AmountCents);

    /// <summary>Issues a voucher worth the amount and returns it with its code, which is shown once.</summary>
    public static Result<(GiftVoucher Voucher, string Code)> Issue(Guid shopId, long amountCents, int validMonths, Guid staffId, Guid deviceId, Guid? saleId, string reference, DateTimeOffset now, string? paidWith = null)
    {
        if (amountCents <= 0 || amountCents > MaxCents) return Result<(GiftVoucher, string)>.Fail("voucher.amount_invalid", "A voucher is worth more than zero and at most R5000.");
        if (validMonths is < 1 or > 60) return Result<(GiftVoucher, string)>.Fail("voucher.validity_invalid", "A voucher is valid for 1 to 60 months.");

        if (paidWith is not null && paidWith is not (PaymentMethods.Cash or PaymentMethods.Card or PaymentMethods.Qr)) return Result<(GiftVoucher, string)>.Fail("voucher.method_invalid", "Pay for a voucher with cash, card or QR.");

        var code = NewCode();
        var voucher = new GiftVoucher { ShopId = shopId, PaidWithMethod = paidWith, InitialCents = amountCents, CodeHash = Hash(code), CodeLast4 = Normalise(code)[^4..], ExpiresOn = BusinessDay.Of(now).AddMonths(validMonths), CreatedAt = now, CreatedBy = staffId, DeviceId = deviceId };
        voucher._entries.Add(new VoucherEntry(voucher.Id, VoucherEntryType.Issue, amountCents, saleId, reference, staffId, now));
        return Result<(GiftVoucher, string)>.Ok((voucher, code));
    }

    public Result Redeem(long amountCents, Guid? saleId, string reference, Guid staffId, DateTimeOffset now)
    {
        if (IsVoid) return Result.Fail("voucher.void", "This voucher has been cancelled.");
        if (ExpiresOn is { } expiry && BusinessDay.Of(now) > expiry) return Result.Fail("voucher.expired", $"This voucher expired on {expiry:yyyy-MM-dd}.");
        if (amountCents <= 0) return Result.Fail("voucher.amount_invalid", "The amount must be more than zero.");
        if (amountCents > BalanceCents) return Result.Fail("voucher.insufficient", $"Only {BalanceCents} cents is left on this voucher.");
        if (_entries.Any(e => e.Reference == reference && e.Type == VoucherEntryType.Redeem)) return Result.Ok();   // the same redemption again changes nothing
        _entries.Add(new VoucherEntry(Id, VoucherEntryType.Redeem, -amountCents, saleId, reference, staffId, now));
        Touch();
        return Result.Ok();
    }

    public void Cancel()
    {
        IsVoid = true;
        Touch();
    }

    public static string NewCode()
    {
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }

    public static string Normalise(string? code) => new((code ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(code)))).ToLowerInvariant();
}

public sealed class VoucherEntry : Entity
{
    private VoucherEntry() { }

    public VoucherEntry(Guid voucherId, VoucherEntryType type, long amountCents, Guid? saleId, string reference, Guid staffId, DateTimeOffset at)
    {
        VoucherId = voucherId;
        Type = type;
        AmountCents = amountCents;
        SaleId = saleId;
        Reference = reference;
        CreatedBy = staffId;
        CreatedAt = at;
    }

    public Guid VoucherId { get; private set; }
    public VoucherEntryType Type { get; private set; }

    /// <summary>Signed: positive adds value (issue, refund), negative takes it (redeem).</summary>
    public long AmountCents { get; private set; }

    public Guid? SaleId { get; private set; }

    /// <summary>What makes an entry unique, such as the sale or till event that redeemed it, so a repeat is recognised.</summary>
    public string Reference { get; private set; } = default!;
}

/// <summary>B6-3: the serial number of an item that was sold, searchable later (for warranty or a recall). A serial can be sold once.</summary>
public sealed class SerialNumberRecord : Entity
{
    private SerialNumberRecord() { }

    public SerialNumberRecord(Guid shopId, Guid productId, string serial, Guid saleId, DateTimeOffset soldAt, Guid staffId)
    {
        ShopId = shopId;
        ProductId = productId;
        Serial = serial;
        SaleId = saleId;
        SoldAt = soldAt;
        CreatedBy = staffId;
        CreatedAt = soldAt;
    }

    public Guid ShopId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Serial { get; private set; } = default!;
    public Guid SaleId { get; private set; }
    public DateTimeOffset SoldAt { get; private set; }

    public static Result<string> Clean(string? serial)
    {
        var text = serial?.Trim().ToUpperInvariant() ?? "";
        return text.Length is >= 3 and <= 50 && text.All(c => char.IsLetterOrDigit(c) || c is '-' or '/' or '.')
            ? Result<string>.Ok(text)
            : Result<string>.Fail("serial.invalid", "A serial number is 3 to 50 letters, digits, dashes, dots or slashes.");
    }
}
