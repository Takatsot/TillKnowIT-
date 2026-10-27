using System.Text.Json;
using TillKnowIT.SharedKernel;

namespace Customers.Domain;

/// <summary>South African phone numbers, as the customer list matches people by them. 0821234567, 27821234567 and +27 82 123 4567 are the same person.</summary>
public static class SaPhone
{
    /// <summary>The number as +27 followed by nine digits, or null if it is not a South African mobile or landline number.</summary>
    public static string? Normalise(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0027", StringComparison.Ordinal)) digits = digits[4..];
        else if (digits.StartsWith("27", StringComparison.Ordinal) && digits.Length == 11) digits = digits[2..];
        else if (digits.StartsWith('0') && digits.Length == 10) digits = digits[1..];
        return digits.Length == 9 && digits[0] != '0' ? "+27" + digits : null;
    }
}

public sealed record CustomerInput(string? Name, string? Phone, string? Email, bool MessageConsent, string? ConsentSource);

/// <summary>
/// B7-1: a person who buys from the shop, found by phone number. Consent to be messaged (slips, appointment reminders) is a separate, explicit yes
/// with when and how it was given; without it nobody sends them anything.
/// </summary>
public sealed class Customer : VersionedEntity
{
    private Customer() { }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Phone { get; private set; } = default!;
    public string? Email { get; private set; }
    public bool MessageConsent { get; private set; }
    public DateTimeOffset? ConsentAt { get; private set; }
    public string? ConsentSource { get; private set; }

    /// <summary>Set when the owner merged this customer into another: they are the same person and the other record carries on.</summary>
    public Guid? MergedIntoId { get; private set; }

    public bool IsMerged => MergedIntoId is not null;

    public static Result<Customer> Create(Guid id, Guid shopId, Guid? staffId, Guid? deviceId, CustomerInput i, DateTimeOffset now)
    {
        var valid = Check(i);
        if (!valid.IsSuccess) return Result<Customer>.Fail(valid.Error!.Code, valid.Error.Message);

        var customer = new Customer { ShopId = shopId };
        customer.StampFromDevice(id, now, staffId, deviceId);
        customer.Apply(i, now, firstTime: true);
        return Result<Customer>.Ok(customer);
    }

    public Result Update(CustomerInput i, DateTimeOffset now)
    {
        if (IsMerged) return Result.Fail("customer.merged", "This customer was merged into another.");
        var valid = Check(i);
        if (!valid.IsSuccess) return valid;
        Apply(i, now, firstTime: false);
        Touch();
        return Result.Ok();
    }

    public void SetConsent(bool consent, string? source, DateTimeOffset now)
    {
        if (consent == MessageConsent) return;
        MessageConsent = consent;
        ConsentAt = now;
        ConsentSource = source?.Trim() is { Length: > 0 } s ? s[..Math.Min(s.Length, 40)] : null;
        Touch();
    }

    public void MergeInto(Guid targetId)
    {
        MergedIntoId = targetId;
        Touch();
    }

    private void Apply(CustomerInput i, DateTimeOffset now, bool firstTime)
    {
        Name = i.Name!.Trim();
        Phone = SaPhone.Normalise(i.Phone)!;
        Email = string.IsNullOrWhiteSpace(i.Email) ? null : i.Email.Trim();
        // Consent is only ever changed by saying so: an update that leaves it as it was keeps when it was given.
        if (firstTime || i.MessageConsent != MessageConsent)
        {
            MessageConsent = i.MessageConsent;
            ConsentAt = i.MessageConsent || firstTime ? now : ConsentAt;
            ConsentSource = i.MessageConsent ? (i.ConsentSource?.Trim() is { Length: > 0 } s ? s[..Math.Min(s.Length, 40)] : "till") : ConsentSource;
        }
    }

    private static Result Check(CustomerInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Name) || i.Name.Trim().Length > 200) return Result.Fail("customer.name_invalid", "A customer needs a name (up to 200 characters).");
        if (SaPhone.Normalise(i.Phone) is null) return Result.Fail("customer.phone_invalid", "That is not a South African phone number.");
        if (i.Email is { Length: > 0 } e && (e.Length > 320 || !e.Contains('@') || e.Contains(' '))) return Result.Fail("customer.email_invalid", "That email address is not valid.");
        return Result.Ok();
    }
}

public enum LoyaltyEntryType { Earn, Redeem, Adjust, Reverse, Merge }

/// <summary>
/// B7-2: points are append-only entries, like the credit book: the balance is their sum, so it is always right and can always be explained.
/// The reference is unique per customer, so a repeated event (a sale that syncs twice) can never earn twice.
/// </summary>
public sealed class LoyaltyEntry : Entity
{
    private LoyaltyEntry() { }

    public LoyaltyEntry(Guid customerId, Guid shopId, LoyaltyEntryType type, int points, string reference, Guid? saleId, string? note, Guid? staffId, DateTimeOffset at)
    {
        CustomerId = customerId;
        ShopId = shopId;
        Type = type;
        Points = points;
        Reference = reference;
        SaleId = saleId;
        Note = note;
        CreatedBy = staffId;
        CreatedAt = at;
    }

    public Guid CustomerId { get; private set; }
    public Guid ShopId { get; private set; }
    public LoyaltyEntryType Type { get; private set; }

    /// <summary>Signed: earning adds, redeeming and reversing take away.</summary>
    public int Points { get; private set; }

    public string Reference { get; private set; } = default!;
    public Guid? SaleId { get; private set; }
    public string? Note { get; private set; }
}

/// <summary>
/// The shop's loyalty rules from its pack settings: PointsPerRand points for each rand spent, and each point worth CentsPerPoint when redeemed,
/// with a minimum to redeem in one go. A shop that has not set them gets 1 point per rand, 10 cents a point and a minimum of 100 points.
/// </summary>
public sealed record LoyaltyRules(int PointsPerRand = 1, int CentsPerPoint = 10, int MinRedeemPoints = 100)
{
    public static LoyaltyRules FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new LoyaltyRules();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            int Int(string name, int fallback, int min, int max) => r.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) && n >= min && n <= max ? n : fallback;
            return new LoyaltyRules(Int("pointsPerRand", 1, 0, 100), Int("centsPerPoint", 10, 1, 10_000), Int("minRedeemPoints", 100, 1, 1_000_000));
        }
        catch (JsonException) { return new LoyaltyRules(); }
    }

    /// <summary>Points for a sale: whole rands only, so R49.99 earns 49 points at the default rate. Never negative.</summary>
    public int Earned(long totalCents) => totalCents <= 0 ? 0 : (int)Math.Min(int.MaxValue, totalCents / 100 * PointsPerRand);

    public long ValueCents(int points) => (long)points * CentsPerPoint;

    /// <summary>What is left to take back when part of a sale is refunded: the same share of the points as of the money, rounded down.</summary>
    public static int ShareOf(int points, long refundCents, long saleTotalCents) =>
        saleTotalCents <= 0 || points <= 0 ? 0 : (int)Math.Min(points, (long)points * Math.Min(refundCents, saleTotalCents) / saleTotalCents);
}
