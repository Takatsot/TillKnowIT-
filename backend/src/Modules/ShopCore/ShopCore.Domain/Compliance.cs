using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public enum ComplianceStatus { Valid, ExpiringSoon, Expired }

/// <summary>A licence, permit or certificate. The file itself lives in encrypted storage; this row holds what is needed to remind the owner.</summary>
public sealed class ComplianceDocument : VersionedEntity
{
    private ComplianceDocument() { }

    public static Result<ComplianceDocument> Record(Guid id, Guid shopId, Guid staffId, Guid deviceId, string typeCode, string fileUrl,
        string originalName, DateOnly? expiresOn, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            return Result<ComplianceDocument>.Fail("compliance.type_required", "Choose what kind of document this is.");
        // A document that expired years ago is almost certainly a typing mistake.
        if (expiresOn is { } e && e < BusinessDay.Of(now).AddYears(-10))
            return Result<ComplianceDocument>.Fail("compliance.expiry_invalid", "That expiry date is too far in the past.");

        var doc = new ComplianceDocument { ShopId = shopId, TypeCode = typeCode, FileUrl = fileUrl, OriginalName = originalName, ExpiresOn = expiresOn };
        doc.StampFromDevice(id, now, staffId, deviceId);
        return Result<ComplianceDocument>.Ok(doc);
    }

    public Guid ShopId { get; private set; }
    public string TypeCode { get; private set; } = default!;
    public string FileUrl { get; private set; } = default!;
    public string OriginalName { get; private set; } = default!;
    public DateOnly? ExpiresOn { get; private set; }
}

public static class ComplianceRules
{
    public const int FirstReminderDays = 30;
    public const int FinalReminderDays = 7;

    public static int? DaysLeft(DateOnly? expiresOn, DateOnly today) => expiresOn is { } e ? e.DayNumber - today.DayNumber : null;

    public static ComplianceStatus StatusOf(DateOnly? expiresOn, DateOnly today) => DaysLeft(expiresOn, today) switch
    {
        null => ComplianceStatus.Valid,
        < 0 => ComplianceStatus.Expired,
        <= FirstReminderDays => ComplianceStatus.ExpiringSoon,
        _ => ComplianceStatus.Valid,
    };

    /// <summary>
    /// Which reminder is due now: 30 days before expiry, 7 days before, or the day it has expired (0).
    /// If the worker was down on the exact day, the reminder is still due the next time it runs, and only the most
    /// urgent one is returned so the owner never gets two at once. Each reminder is sent once (see the dedupe key).
    /// </summary>
    public static int? ReminderDue(DateOnly? expiresOn, DateOnly today) => DaysLeft(expiresOn, today) switch
    {
        null => null,
        < 0 => 0,
        <= FinalReminderDays => FinalReminderDays,
        <= FirstReminderDays => FirstReminderDays,
        _ => null,
    };

    /// <summary>One key per document and reminder, so a restart or a retry can never send the same reminder twice.</summary>
    public static string DedupeKey(Guid documentId, int reminder) => $"compliance:{documentId:N}:{reminder}";
}
