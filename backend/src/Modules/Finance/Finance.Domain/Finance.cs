using TillKnowIT.SharedKernel;

namespace Finance.Domain;

/// <summary>
/// G4-1: the owner's yes to share a shop's sales history with one named finance partner. It can be withdrawn at any time, and from then on nothing more is shared.
/// We do not lend: the partner decides and lends; we only pass on what the owner agreed to.
/// </summary>
public sealed class CreditConsent : VersionedEntity
{
    private CreditConsent() { }

    public Guid ShopId { get; private set; }
    public string PartnerCode { get; private set; } = default!;
    public int Months { get; private set; }
    public Guid GrantedBy { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public bool IsActive => WithdrawnAt is null;

    public static Result<CreditConsent> Grant(Guid shopId, string? partnerCode, int months, Guid personId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(partnerCode)) return Result<CreditConsent>.Fail("consent.partner_required", "Choose the finance partner.");
        if (months is < 1 or > 24) return Result<CreditConsent>.Fail("consent.months_invalid", "Share between 1 and 24 months of sales.");
        var c = new CreditConsent { ShopId = shopId, PartnerCode = partnerCode.Trim().ToLowerInvariant(), Months = months, GrantedBy = personId, GrantedAt = now };
        c.StampFromDevice(Guid.CreateVersion7(), now, personId, null);
        return Result<CreditConsent>.Ok(c);
    }

    public Result Withdraw(DateTimeOffset now)
    {
        if (!IsActive) return Result.Fail("consent.already_withdrawn", "This consent was already withdrawn.");
        WithdrawnAt = now;
        Touch();
        return Result.Ok();
    }
}

/// <summary>Append-only record of every time sales history was passed to a partner, so the owner can see exactly what was shared and when.</summary>
public sealed class DataShareRecord : Entity
{
    private DataShareRecord() { }

    public Guid ConsentId { get; private set; }
    public Guid ShopId { get; private set; }
    public string PartnerCode { get; private set; } = default!;
    public int Months { get; private set; }
    public long TotalSalesCents { get; private set; }

    public DataShareRecord(Guid consentId, Guid shopId, string partnerCode, int months, long totalSalesCents, DateTimeOffset at)
    {
        ConsentId = consentId;
        ShopId = shopId;
        PartnerCode = partnerCode;
        Months = months;
        TotalSalesCents = totalSalesCents;
        CreatedAt = at;
    }
}

public enum ApplicationStatus { Submitted, Approved, Declined }

/// <summary>An application for stock on credit. The partner's decision is theirs; we record it.</summary>
public sealed class CreditApplication : VersionedEntity
{
    private CreditApplication() { }

    public Guid ShopId { get; private set; }
    public Guid ConsentId { get; private set; }
    public string PartnerCode { get; private set; } = default!;
    public long RequestedCents { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public string? PartnerReference { get; private set; }
    public long ApprovedCents { get; private set; }
    public int TermDays { get; private set; }
    public string? DecisionReason { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    public static Result<CreditApplication> Submit(Guid id, Guid shopId, CreditConsent consent, long requestedCents, Guid personId, DateTimeOffset now)
    {
        if (!consent.IsActive) return Result<CreditApplication>.Fail("consent.withdrawn", "The consent to share your sales history was withdrawn.");
        if (requestedCents < 10_000 || requestedCents > 100_000_00) return Result<CreditApplication>.Fail("credit.amount_invalid", "Ask for between R100 and R100 000.");
        var a = new CreditApplication { ShopId = shopId, ConsentId = consent.Id, PartnerCode = consent.PartnerCode, RequestedCents = requestedCents, Status = ApplicationStatus.Submitted };
        a.StampFromDevice(id, now, personId, null);
        return Result<CreditApplication>.Ok(a);
    }

    public void Decide(bool approved, string? reference, long approvedCents, int termDays, string? reason, DateTimeOffset now)
    {
        Status = approved ? ApplicationStatus.Approved : ApplicationStatus.Declined;
        PartnerReference = reference;
        ApprovedCents = approved ? approvedCents : 0;
        TermDays = approved ? termDays : 0;
        DecisionReason = reason;
        DecidedAt = now;
        Touch();
    }
}

public enum DrawStatus { Disbursed, Repaid }

/// <summary>Stock bought on credit: the partner paid the supplier and the shop repays the partner. Principal is what the supplier was paid; RepayCents is what the shop owes back.</summary>
public sealed class CreditDraw : VersionedEntity
{
    private CreditDraw() { }

    public Guid ApplicationId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid OrderId { get; private set; }
    public string SupplierName { get; private set; } = default!;
    public long PrincipalCents { get; private set; }
    public long RepayCents { get; private set; }
    public long RepaidCents { get; private set; }
    public DateOnly DueOn { get; private set; }
    public string PartnerReference { get; private set; } = default!;
    public DrawStatus Status { get; private set; }

    public long OutstandingCents => Math.Max(0, RepayCents - RepaidCents);

    public static CreditDraw Disburse(CreditApplication app, Guid orderId, string supplierName, long principalCents, long repayCents, DateOnly dueOn, string reference, Guid personId, DateTimeOffset now)
    {
        var d = new CreditDraw
        {
            ApplicationId = app.Id, ShopId = app.ShopId, OrderId = orderId, SupplierName = supplierName, PrincipalCents = principalCents, RepayCents = repayCents,
            DueOn = dueOn, PartnerReference = reference, Status = DrawStatus.Disbursed,
        };
        d.StampFromDevice(Guid.CreateVersion7(), now, personId, null);
        return d;
    }

    /// <summary>Takes a repayment into account. Overpaying is kept at what is owed; the rest is the partner's to refund.</summary>
    public void Repay(long cents)
    {
        RepaidCents += cents;
        if (RepaidCents >= RepayCents) Status = DrawStatus.Repaid;
        Touch();
    }

    /// <summary>The part of the principal still owed, in proportion to what is still outstanding. Credit comes back as the shop repays.</summary>
    public long OutstandingPrincipalCents => Status == DrawStatus.Repaid || RepayCents == 0 ? 0 : (PrincipalCents * OutstandingCents + RepayCents / 2) / RepayCents;
}

/// <summary>A repayment the partner told us about. The partner's reference makes a repeated callback count once.</summary>
public sealed class CreditRepayment : Entity
{
    private CreditRepayment() { }

    public Guid DrawId { get; private set; }
    public long AmountCents { get; private set; }
    public string Reference { get; private set; } = default!;
    public DateTimeOffset PaidAt { get; private set; }

    public CreditRepayment(Guid drawId, long amountCents, string reference, DateTimeOffset paidAt)
    {
        DrawId = drawId;
        AmountCents = amountCents;
        Reference = reference;
        PaidAt = paidAt;
        CreatedAt = paidAt;
    }
}
