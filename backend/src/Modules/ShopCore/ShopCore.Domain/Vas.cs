using System.Text.RegularExpressions;
using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public enum VasProductType { Airtime, Data, Electricity }

public enum VasStatus
{
    /// <summary>Saved, partner not yet answered.</summary>
    Pending,
    Completed,
    /// <summary>The partner said no. Nothing was bought, so the customer's money goes back to them.</summary>
    Failed,
    /// <summary>The partner did not answer, so we do not know if it went through. Never retried automatically: that could buy twice.</summary>
    Unknown,
}

public static partial class VasRules
{
    /// <summary>Placeholder limits and commission until the licensed partner's rate card is signed. Each is a setting, not a promise.</summary>
    public static (long Min, long Max) Limits(VasProductType type) => type switch
    {
        VasProductType.Airtime => (500, 100_000),        // R5 to R1000
        VasProductType.Data => (500, 100_000),
        _ => (2_000, 200_000),                           // electricity: R20 to R2000
    };

    public static int DefaultCommissionBps(VasProductType type) => type switch
    {
        VasProductType.Airtime => 300,
        VasProductType.Data => 300,
        _ => 150,
    };

    /// <summary>Commission is a share of the amount in basis points (100 = 1%), rounded down to the cent.</summary>
    public static long CommissionCents(long amountCents, int basisPoints) => amountCents * basisPoints / 10_000;

    /// <summary>Airtime and data go to a phone number; electricity goes to an 11-digit meter number. Returns the cleaned recipient.</summary>
    public static Result<string> CheckRecipient(VasProductType type, string? recipient)
    {
        var r = recipient?.Trim() ?? "";
        if (type == VasProductType.Electricity)
        {
            var meter = r.Replace(" ", "").Replace("-", "");
            return MeterPattern().IsMatch(meter) ? Result<string>.Ok(meter) : Result<string>.Fail("vas.recipient_invalid", "A meter number is 11 digits.");
        }

        var digits = new string(r.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = "+" + digits[2..];
        if (digits.StartsWith('0')) digits = "+27" + digits[1..];
        else if (digits.Length > 0 && !digits.StartsWith('+')) digits = "+" + digits;
        return PhonePattern().IsMatch(digits) ? Result<string>.Ok(digits) : Result<string>.Fail("vas.recipient_invalid", "That phone number is not valid.");
    }

    [GeneratedRegex(@"^\d{11}$")]
    private static partial Regex MeterPattern();

    [GeneratedRegex(@"^\+\d{9,15}$")]
    private static partial Regex PhonePattern();
}

public sealed record VasSaleInput(Guid Id, Guid ShopId, Guid StaffId, Guid DeviceId, VasProductType Type, string Recipient, long AmountCents,
    string? ProductCode, int CommissionBps, DateTimeOffset Now);

/// <summary>A prepaid sale through the licensed partner. Online only: it needs the partner to answer.</summary>
public sealed class VasSale : VersionedEntity
{
    private VasSale() { }

    public static Result<VasSale> Create(VasSaleInput i)
    {
        var (min, max) = VasRules.Limits(i.Type);
        if (i.AmountCents < min || i.AmountCents > max)
            return Result<VasSale>.Fail("vas.amount_invalid", $"The amount must be between R{min / 100} and R{max / 100}.");
        if (i.AmountCents % 100 != 0)
            return Result<VasSale>.Fail("vas.amount_invalid", "The amount must be whole rand.");
        var recipient = VasRules.CheckRecipient(i.Type, i.Recipient);
        if (!recipient.IsSuccess) return Result<VasSale>.Fail(recipient.Error!.Code, recipient.Error.Message);
        if (i.Type == VasProductType.Data && string.IsNullOrWhiteSpace(i.ProductCode))
            return Result<VasSale>.Fail("vas.product_required", "Choose a data bundle.");
        if (i.CommissionBps is < 0 or > 2_000)
            return Result<VasSale>.Fail("vas.commission_invalid", "The commission rate is not valid.");

        var sale = new VasSale
        {
            ShopId = i.ShopId, ProductType = i.Type, Recipient = recipient.Value!, AmountCents = i.AmountCents,
            ProductCode = string.IsNullOrWhiteSpace(i.ProductCode) ? null : i.ProductCode.Trim(),
            CommissionCents = VasRules.CommissionCents(i.AmountCents, i.CommissionBps),
        };
        sale.StampFromDevice(i.Id, i.Now, i.StaffId, i.DeviceId);
        return Result<VasSale>.Ok(sale);
    }

    public Guid ShopId { get; private set; }
    public VasProductType ProductType { get; private set; }
    public string Recipient { get; private set; } = default!;
    public string? ProductCode { get; private set; }
    public long AmountCents { get; private set; }
    public long CommissionCents { get; private set; }
    public VasStatus Status { get; private set; } = VasStatus.Pending;
    public string? PartnerRef { get; private set; }

    /// <summary>The electricity token the customer enters at their meter. Shown once to the cashier; kept so a lost slip can be reprinted.</summary>
    public string? Token { get; private set; }
    public string? FailureReason { get; private set; }

    public void Complete(string partnerRef, string? token)
    {
        Status = VasStatus.Completed;
        PartnerRef = partnerRef;
        Token = token;
        Touch();
    }

    public void Fail(string reason)
    {
        Status = VasStatus.Failed;
        FailureReason = reason.Length > 300 ? reason[..300] : reason;
        Touch();
    }

    public void MarkUnknown(string reason)
    {
        Status = VasStatus.Unknown;
        FailureReason = reason.Length > 300 ? reason[..300] : reason;
        Touch();
    }

    /// <summary>Commission only counts for sales that went through.</summary>
    public long EarnedCommissionCents => Status == VasStatus.Completed ? CommissionCents : 0;
}
