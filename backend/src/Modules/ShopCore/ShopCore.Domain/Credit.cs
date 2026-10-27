using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public sealed record CreditAccountInput(
    Guid Id, Guid ShopId, Guid StaffId, Guid DeviceId, string Name, string? Phone, long? CreditLimitCents, DateTimeOffset UpdatedAt);

/// <summary>A customer who buys on credit. The balance is never stored: it is the sum of the account's entries.</summary>
public sealed class CreditAccount : VersionedEntity
{
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromHours(24);

    private CreditAccount() { }

    public static Result<CreditAccountInput> Check(CreditAccountInput i, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(i.Name) || i.Name.Trim().Length > 200)
            return Result<CreditAccountInput>.Fail("account.name_invalid", "A customer needs a name (up to 200 characters).");
        if (i.Phone is not null && i.Phone.Trim().Length > 20)
            return Result<CreditAccountInput>.Fail("account.phone_invalid", "That phone number is too long.");
        if (i.CreditLimitCents < 0)
            return Result<CreditAccountInput>.Fail("account.limit_invalid", "A credit limit cannot be negative.");
        if (i.UpdatedAt > now + MaxClockSkew)
            return Result<CreditAccountInput>.Fail("account.date_invalid", "The change is dated in the future.");
        return Result<CreditAccountInput>.Ok(i with { Name = i.Name.Trim(), Phone = string.IsNullOrWhiteSpace(i.Phone) ? null : i.Phone.Trim() });
    }

    public static CreditAccount Record(CreditAccountInput i)
    {
        var account = new CreditAccount { ShopId = i.ShopId };
        account.StampFromDevice(i.Id, i.UpdatedAt, i.StaffId, i.DeviceId);
        account.Apply(i);
        return account;
    }

    public DateTimeOffset LastChangedAt => UpdatedAt ?? CreatedAt;

    /// <summary>Last change wins, by the time on the change, like products.</summary>
    public Result Update(CreditAccountInput i)
    {
        if (i.UpdatedAt <= LastChangedAt)
            return Result.Fail("account.stale", "A newer change to this customer is already saved.");
        Apply(i);
        StampUpdate(i.UpdatedAt);
        return Result.Ok();
    }

    private void Apply(CreditAccountInput i)
    {
        Name = i.Name.Trim();
        Phone = string.IsNullOrWhiteSpace(i.Phone) ? null : i.Phone.Trim();
        CreditLimitCents = i.CreditLimitCents;
    }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Phone { get; private set; }

    /// <summary>Optional. Going over it warns the cashier; it never blocks a sale.</summary>
    public long? CreditLimitCents { get; private set; }
}

public enum CreditEntryType { Sale, Payment, Cancel, Refund }

public sealed record CreditEntryInput(
    Guid Id, Guid AccountId, CreditEntryType Type, long AmountCents, Guid? SaleId, string? PaymentMethodCode,
    Guid StaffId, Guid DeviceId, DateTimeOffset CreatedAt);

/// <summary>
/// Append only and signed: a credit sale adds to what the customer owes (positive), a payment or a
/// cancelled sale takes away (negative). Balance = sum of entries.
/// </summary>
public sealed class CreditEntry : Entity
{
    private CreditEntry() { }

    public static Result<CreditEntry> Record(CreditEntryInput i)
    {
        if (i.AmountCents == 0)
            return Result<CreditEntry>.Fail("credit.amount_zero", "A credit entry cannot be zero.");

        var signOk = i.Type == CreditEntryType.Sale ? i.AmountCents > 0 : i.AmountCents < 0;
        if (!signOk)
            return Result<CreditEntry>.Fail("credit.amount_sign", $"A {i.Type} entry has the wrong sign.");

        if (i.Type == CreditEntryType.Payment)
        {
            if (i.SaleId is not null)
                return Result<CreditEntry>.Fail("credit.sale_unexpected", "A payment is not tied to a sale.");
            if (i.PaymentMethodCode is not (PaymentMethods.Cash or PaymentMethods.Card or PaymentMethods.Qr))
                return Result<CreditEntry>.Fail("credit.payment_method", "A payment is made with cash, card or QR.");
        }
        else if (i.SaleId is null)
        {
            return Result<CreditEntry>.Fail("credit.sale_required", "Sale and cancel entries must point at their sale.");
        }

        var entry = new CreditEntry
        {
            AccountId = i.AccountId,
            Type = i.Type,
            AmountCents = i.AmountCents,
            SaleId = i.SaleId,
            PaymentMethodCode = i.Type == CreditEntryType.Payment ? i.PaymentMethodCode : null,
        };
        entry.StampFromDevice(i.Id, i.CreatedAt, i.StaffId, i.DeviceId);
        return Result<CreditEntry>.Ok(entry);
    }

    public Guid AccountId { get; private set; }
    public CreditEntryType Type { get; private set; }

    /// <summary>Signed cents: positive adds to the debt, negative reduces it.</summary>
    public long AmountCents { get; private set; }
    public Guid? SaleId { get; private set; }

    /// <summary>Only for payments. Cash payments count towards the till's cash-up.</summary>
    public string? PaymentMethodCode { get; private set; }
}

public static class CreditBook
{
    public static long Balance(IEnumerable<long> signedEntryAmounts) => signedEntryAmounts.Sum();

    /// <summary>True when the customer owes more than their limit. No limit means never over.</summary>
    public static bool OverLimit(long balanceCents, long? limitCents) => limitCents is not null && balanceCents > limitCents;
}
