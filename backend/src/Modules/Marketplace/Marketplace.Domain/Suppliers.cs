using System.Text.RegularExpressions;
using TillKnowIT.SharedKernel;

namespace Marketplace.Domain;

public enum SupplierStatus
{
    Pending,
    Verified,
    Rejected,
    Suspended,
    /// <summary>A supplier a shop added itself, so it can order by WhatsApp or email. Not on the platform: no portal, no catalogue.</summary>
    Unlisted,
}

public enum PaymentTerms { OnDelivery, InApp, Both }

/// <summary>The days a supplier delivers to an area. Stored as one number.</summary>
[Flags]
public enum DeliveryDays { None = 0, Mon = 1, Tue = 2, Wed = 4, Thu = 8, Fri = 16, Sat = 32, Sun = 64 }

/// <summary>M1-2: what the platform admin ticks before verifying a supplier. Every item is needed.</summary>
public static class SupplierVerification
{
    public static readonly string[] Items = ["registration_checked", "contact_confirmed", "delivery_capability", "prices_reviewed"];

    public static IReadOnlyList<string> Missing(IEnumerable<string> done) => Items.Except(done).ToList();
}

public sealed record SupplierApplication(
    Guid ApplicantPersonId, Guid OrganisationId, string Name, string? RegistrationNo, string ContactName,
    string? Phone, string? Whatsapp, string? Email, int AreaCount);

public sealed partial class Supplier : VersionedEntity
{
    private Supplier() { }

    /// <summary>M1-1: a supplier applies. It is pending until verified, and needs a way to be reached and somewhere to deliver.</summary>
    public static Result<Supplier> Apply(SupplierApplication a, DateTimeOffset now)
    {
        var common = CheckCommon(a.Name, a.Phone, a.Whatsapp, a.Email);
        if (!common.IsSuccess) return Result<Supplier>.Fail(common.Error!.Code, common.Error.Message);
        if (string.IsNullOrWhiteSpace(a.ContactName))
            return Result<Supplier>.Fail("supplier.contact_required", "Give the name of a contact person.");
        if (a.RegistrationNo is not null && !RegistrationPattern().IsMatch(a.RegistrationNo.Trim()))
            return Result<Supplier>.Fail("supplier.registration_invalid", "A registration number looks like 2015/123456/07.");
        if (a.AreaCount < 1)
            return Result<Supplier>.Fail("supplier.areas_required", "Choose at least one area you deliver to.");

        var supplier = new Supplier
        {
            OrganisationId = a.OrganisationId, ApplicantPersonId = a.ApplicantPersonId, Name = a.Name.Trim(),
            RegistrationNo = string.IsNullOrWhiteSpace(a.RegistrationNo) ? null : a.RegistrationNo.Trim(),
            ContactName = a.ContactName.Trim(), Phone = Clean(a.Phone), Whatsapp = Clean(a.Whatsapp), Email = Clean(a.Email),
            Status = SupplierStatus.Pending,
        };
        supplier.StampFromDevice(Guid.CreateVersion7(), now, a.ApplicantPersonId, null);
        return Result<Supplier>.Ok(supplier);
    }

    /// <summary>A shop adds a supplier it already buys from, so orders can go by WhatsApp or email.</summary>
    public static Result<Supplier> Unlisted(Guid shopId, string name, string? phone, string? whatsapp, string? email, DateTimeOffset now)
    {
        var common = CheckCommon(name, phone, whatsapp, email);
        if (!common.IsSuccess) return Result<Supplier>.Fail(common.Error!.Code, common.Error.Message);
        var supplier = new Supplier
        {
            CreatedByShopId = shopId, Name = name.Trim(), ContactName = name.Trim(),
            Phone = Clean(phone), Whatsapp = Clean(whatsapp), Email = Clean(email), Status = SupplierStatus.Unlisted,
        };
        supplier.StampFromDevice(Guid.CreateVersion7(), now, null, null);
        return Result<Supplier>.Ok(supplier);
    }

    private static Result CheckCommon(string? name, string? phone, string? whatsapp, string? email)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            return Result.Fail("supplier.name_invalid", "A supplier needs a name (up to 200 characters).");
        if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(whatsapp) && string.IsNullOrWhiteSpace(email))
            return Result.Fail("supplier.contact_required", "Give a phone number, WhatsApp number or email so shops can reach the supplier.");
        if (!string.IsNullOrWhiteSpace(email) && !EmailPattern().IsMatch(email.Trim()))
            return Result.Fail("supplier.email_invalid", "That email address is not valid.");
        return Result.Ok();
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public Result Verify(Guid adminPersonId, IEnumerable<string> checklistDone, DateTimeOffset now)
    {
        if (Status is SupplierStatus.Unlisted) return Result.Fail("supplier.not_on_platform", "This supplier is not on the platform.");
        if (Status == SupplierStatus.Suspended) return Result.Fail("supplier.suspended", "This supplier is suspended.");
        var done = checklistDone.ToList();
        var missing = SupplierVerification.Missing(done);
        if (missing.Count > 0) return Result.Fail("supplier.checklist_incomplete", $"Still to check: {string.Join(", ", missing)}.");

        Status = SupplierStatus.Verified;
        VerifiedAt = now;
        VerifiedBy = adminPersonId;
        RejectionReason = null;
        ChecklistDone = string.Join(',', SupplierVerification.Items);
        Touch();
        return Result.Ok();
    }

    public Result Reject(Guid adminPersonId, string? reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("supplier.reason_required", "Say why the supplier is rejected, so they can fix it and apply again.");
        if (Status != SupplierStatus.Pending) return Result.Fail("supplier.not_pending", "Only a pending application can be rejected.");
        Status = SupplierStatus.Rejected;
        RejectionReason = reason.Trim();
        VerifiedBy = adminPersonId;
        VerifiedAt = now;
        Touch();
        return Result.Ok();
    }

    public void Suspend()
    {
        Status = SupplierStatus.Suspended;
        Touch();
    }

    /// <summary>M2-2: the order minimum, delivery fee and how shops may pay.</summary>
    public Result SetTerms(long minOrderCents, long deliveryFeeCents, PaymentTerms terms)
    {
        if (minOrderCents < 0 || deliveryFeeCents < 0) return Result.Fail("supplier.terms_invalid", "The minimum order and delivery fee cannot be negative.");
        MinOrderCents = minOrderCents;
        DeliveryFeeCents = deliveryFeeCents;
        PaymentTerms = terms;
        Touch();
        return Result.Ok();
    }

    /// <summary>Only verified suppliers show offers and take orders in the app.</summary>
    public bool IsTrading => Status == SupplierStatus.Verified;

    public Guid? OrganisationId { get; private set; }
    public Guid? ApplicantPersonId { get; private set; }
    public Guid? CreatedByShopId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? RegistrationNo { get; private set; }
    public string ContactName { get; private set; } = default!;
    public string? Phone { get; private set; }
    public string? Whatsapp { get; private set; }
    public string? Email { get; private set; }
    public SupplierStatus Status { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public Guid? VerifiedBy { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? ChecklistDone { get; private set; }
    public long MinOrderCents { get; private set; }
    public long DeliveryFeeCents { get; private set; }
    public PaymentTerms PaymentTerms { get; private set; } = PaymentTerms.Both;

    [GeneratedRegex(@"^\d{4}/\d{4,8}/\d{2}$")]
    private static partial Regex RegistrationPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}

/// <summary>M2-2: where and on which days a supplier delivers. Areas come from the municipalities lookup.</summary>
public sealed class SupplierArea : Entity
{
    private SupplierArea() { }

    public SupplierArea(Guid supplierId, string municipalityCode, DeliveryDays days)
    {
        SupplierId = supplierId;
        MunicipalityCode = municipalityCode;
        Days = days;
    }

    public Guid SupplierId { get; private set; }
    public string MunicipalityCode { get; private set; } = default!;
    public DeliveryDays Days { get; private set; }

    public static Result<List<SupplierArea>> Build(Guid supplierId, IEnumerable<(string Code, DeliveryDays Days)> areas)
    {
        var list = areas.ToList();
        if (list.Count == 0) return Result<List<SupplierArea>>.Fail("supplier.areas_required", "Choose at least one area you deliver to.");
        if (list.Select(a => a.Code).Distinct().Count() != list.Count)
            return Result<List<SupplierArea>>.Fail("supplier.area_duplicate", "An area can be listed only once.");
        if (list.Any(a => a.Days == DeliveryDays.None))
            return Result<List<SupplierArea>>.Fail("supplier.days_required", "Say which days you deliver to each area.");
        return Result<List<SupplierArea>>.Ok(list.Select(a => new SupplierArea(supplierId, a.Code, a.Days)).ToList());
    }

    /// <summary>"Mon,Wed,Fri" to flags. Unknown days are an error, not ignored.</summary>
    public static bool TryParseDays(IEnumerable<string> names, out DeliveryDays days)
    {
        days = DeliveryDays.None;
        foreach (var name in names)
        {
            if (!Enum.TryParse<DeliveryDays>(name, ignoreCase: true, out var d) || d == DeliveryDays.None || !Enum.IsDefined(d)) return false;
            days |= d;
        }
        return true;
    }

    public static IReadOnlyList<string> DayNames(DeliveryDays days) =>
        Enum.GetValues<DeliveryDays>().Where(d => d != DeliveryDays.None && days.HasFlag(d)).Select(d => d.ToString()).ToList();
}
