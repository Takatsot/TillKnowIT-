using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TillKnowIT.SharedKernel;

namespace Wallet.Domain;

public enum SchoolStatus { Pending, Verified, Suspended }
public enum PayoutSchedule { Daily, Weekly, Monthly }

/// <summary>W1-1: a school and the tuckshop (a shop) that sells on its wallets. Verified by the platform before anything can be loaded.</summary>
public sealed class School : VersionedEntity
{
    public const int MaxFeeBasisPoints = 1000;

    private School() { }

    public string Name { get; private set; } = default!;
    public string? EmisNumber { get; private set; }
    public Guid ShopId { get; private set; }
    public SchoolStatus Status { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public Guid? VerifiedBy { get; private set; }
    public PayoutSchedule PayoutSchedule { get; private set; } = PayoutSchedule.Weekly;

    /// <summary>What the platform keeps of each wallet sale, in basis points (150 = 1.5%). Zero until the commercial terms are agreed.</summary>
    public int FeeBasisPoints { get; private set; }

    public bool StaffAccountsEnabled { get; private set; }

    /// <summary>A parent's daily limit applies on top of this school-wide ceiling. Null means no ceiling.</summary>
    public long? DailyCeilingCents { get; private set; }

    /// <summary>Tills that have been offline longer than this split a learner's allowance between them.</summary>
    public int OfflineSplitMinutes { get; private set; } = 60;

    public static Result<School> Register(string? name, string? emis, Guid shopId, DateTimeOffset now)
    {
        var n = name?.Trim() ?? "";
        if (n.Length < 2 || n.Length > 150) return Result<School>.Fail("school.name_invalid", "Give the school's name.");
        var e = string.IsNullOrWhiteSpace(emis) ? null : emis.Trim();
        if (e is not null && !Regex.IsMatch(e, @"^\d{4,12}$")) return Result<School>.Fail("school.emis_invalid", "An EMIS number is 4 to 12 digits.");
        return Result<School>.Ok(new School { Name = n, EmisNumber = e, ShopId = shopId, Status = SchoolStatus.Pending, CreatedAt = now });
    }

    public Result Verify(Guid admin, DateTimeOffset now)
    {
        if (Status == SchoolStatus.Verified) return Result.Ok();
        Status = SchoolStatus.Verified;
        VerifiedAt = now;
        VerifiedBy = admin;
        Touch();
        return Result.Ok();
    }

    public void Suspend()
    {
        Status = SchoolStatus.Suspended;
        Touch();
    }

    public bool IsTrading => Status == SchoolStatus.Verified;

    public Result Configure(int feeBasisPoints, PayoutSchedule schedule, bool staffAccounts, long? dailyCeilingCents, int offlineSplitMinutes)
    {
        if (feeBasisPoints is < 0 or > MaxFeeBasisPoints) return Result.Fail("school.fee_invalid", "The fee is from 0 to 10%.");
        if (dailyCeilingCents is < 0 or > 500_000) return Result.Fail("school.ceiling_invalid", "The daily ceiling is from R0 to R5000.");
        if (offlineSplitMinutes is < 5 or > 480) return Result.Fail("school.split_invalid", "The offline split time is from 5 minutes to 8 hours.");
        FeeBasisPoints = feeBasisPoints;
        PayoutSchedule = schedule;
        StaffAccountsEnabled = staffAccounts;
        DailyCeilingCents = dailyCeilingCents;
        OfflineSplitMinutes = offlineSplitMinutes;
        Touch();
        return Result.Ok();
    }
}

public sealed class SchoolAdmin : Entity
{
    private SchoolAdmin() { }

    public SchoolAdmin(Guid schoolId, Guid personId, DateTimeOffset now)
    {
        SchoolId = schoolId;
        PersonId = personId;
        CreatedAt = now;
    }

    public Guid SchoolId { get; private set; }
    public Guid PersonId { get; private set; }
}

public enum HolderType { Learner, Teacher }
public enum HolderStatus { Active, Left, Erased }

public sealed record HolderInput(string FirstName, string LastName, string? Grade, string? ClassName, HolderType Type);

/// <summary>
/// W1-2: a learner or teacher who can hold a wallet. Only what is needed is kept: name, grade, class and a photo for the till check.
/// A parent links to a learner with the link code the school prints.
/// </summary>
public sealed class Holder : VersionedEntity
{
    public const int MaxGuardians = 4;

    private Holder() { }

    public Guid SchoolId { get; private set; }
    public HolderType Type { get; private set; }
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string? Grade { get; private set; }
    public string? ClassName { get; private set; }
    public string? PhotoUrl { get; private set; }
    public HolderStatus Status { get; private set; }
    public string? LinkCodeHash { get; private set; }

    public string DisplayName => Status == HolderStatus.Erased ? "Removed" : $"{FirstName} {LastName}";

    public static Result<Holder> Create(Guid schoolId, HolderInput i, DateTimeOffset now)
    {
        var first = i.FirstName?.Trim() ?? "";
        var last = i.LastName?.Trim() ?? "";
        if (first.Length == 0 || last.Length == 0) return Result<Holder>.Fail("holder.name_required", "A first name and a last name are needed.");
        if (first.Length > 80 || last.Length > 80) return Result<Holder>.Fail("holder.name_too_long", "Names are at most 80 characters.");
        return Result<Holder>.Ok(new Holder
        {
            SchoolId = schoolId, Type = i.Type, FirstName = first, LastName = last,
            Grade = Clean(i.Grade, 20), ClassName = Clean(i.ClassName, 20), Status = HolderStatus.Active, CreatedAt = now,
        });
    }

    /// <summary>The same person, for spotting duplicates in an import: names and class compared without case, spaces or accents of spelling.</summary>
    public static string DuplicateKey(string first, string last, string? grade, string? className) =>
        string.Join("|", Normalise(first), Normalise(last), Normalise(grade ?? ""), Normalise(className ?? ""));

    public string Key => DuplicateKey(FirstName, LastName, Grade, ClassName);

    public Result Update(string? first, string? last, string? grade, string? className)
    {
        if (Status == HolderStatus.Erased) return Result.Fail("holder.erased", "This person's data has been removed.");
        var f = first?.Trim() ?? FirstName;
        var l = last?.Trim() ?? LastName;
        if (f.Length == 0 || l.Length == 0 || f.Length > 80 || l.Length > 80) return Result.Fail("holder.name_required", "Names are needed, up to 80 characters.");
        FirstName = f;
        LastName = l;
        Grade = grade is null ? Grade : Clean(grade, 20);
        ClassName = className is null ? ClassName : Clean(className, 20);
        Touch();
        return Result.Ok();
    }

    public void SetPhoto(string? url)
    {
        PhotoUrl = url;
        Touch();
    }

    public Result MarkLeft()
    {
        if (Status != HolderStatus.Active) return Result.Fail("holder.not_active", "This person is not active.");
        Status = HolderStatus.Left;
        Touch();
        return Result.Ok();
    }

    /// <summary>
    /// W5-4: personal data goes; the holder row stays (the ledger refers to it) but holds nothing about the person any more.
    /// </summary>
    public void Erase()
    {
        FirstName = "Removed";
        LastName = "Removed";
        Grade = null;
        ClassName = null;
        PhotoUrl = null;
        LinkCodeHash = null;
        Status = HolderStatus.Erased;
        Touch();
    }

    /// <summary>A new link code, shown once. Only its hash is kept, so a lost printout is replaced, never looked up.</summary>
    public string IssueLinkCode()
    {
        var code = LinkCodes.Generate();
        LinkCodeHash = LinkCodes.Hash(code);
        Touch();
        return code;
    }

    public bool LinkCodeMatches(string? code) => LinkCodeHash is not null && code is not null && LinkCodes.Hash(code) == LinkCodeHash;

    private static string? Clean(string? v, int max)
    {
        var t = v?.Trim();
        return string.IsNullOrEmpty(t) ? null : (t.Length > max ? t[..max] : t);
    }

    private static string Normalise(string v) => Regex.Replace(v.Trim().ToLowerInvariant(), @"\s+", " ");
}

public static class LinkCodes
{
    // No I, L, O or U: they are easy to mistake for 1, 1, 0 and V when read off a printout.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    public static string Generate()
    {
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    public static string Normalise(string? code) => new((code ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(code)))).ToLowerInvariant();
}

public enum TokenType { Nfc, QrSecret }
public enum TokenStatus { Active, Blocked }

/// <summary>
/// W1-3 and W3-3: a card or wristband (its NFC UID) or a phone's QR secret linked to a holder. Only a hash of the UID is kept.
/// A lost card is blocked; the replacement links to the same wallet.
/// </summary>
public sealed class HolderToken : Entity
{
    private HolderToken() { }

    public HolderToken(Guid holderId, TokenType type, string rawToken, DateTimeOffset now)
    {
        HolderId = holderId;
        Type = type;
        TokenHash = Hash(rawToken);
        Status = TokenStatus.Active;
        CreatedAt = now;
    }

    public Guid HolderId { get; private set; }
    public TokenType Type { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public TokenStatus Status { get; private set; }
    public DateTimeOffset? BlockedAt { get; private set; }

    /// <summary>The UID is compared without case, colons or spaces, so readers that print it differently still agree.</summary>
    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new string(raw.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray())))).ToLowerInvariant();

    public void Block(DateTimeOffset now)
    {
        if (Status == TokenStatus.Blocked) return;
        Status = TokenStatus.Blocked;
        BlockedAt = now;
    }
}

/// <summary>W2-1: a parent linked to a child, with the consent they gave. Consent is recorded before any of the child's data is shown to them.</summary>
public sealed class Guardianship : Entity
{
    private Guardianship() { }

    public Guardianship(Guid parentPersonId, Guid holderId, string consentVersion, DateTimeOffset now)
    {
        ParentPersonId = parentPersonId;
        HolderId = holderId;
        ConsentVersion = consentVersion;
        ConsentAt = now;
        CreatedAt = now;
    }

    public Guid ParentPersonId { get; private set; }
    public Guid HolderId { get; private set; }
    public DateTimeOffset ConsentAt { get; private set; }
    public string ConsentVersion { get; private set; } = default!;

    /// <summary>W2-4: a push message on every purchase, if the parent wants it.</summary>
    public bool NotifyOnPurchase { get; private set; }

    /// <summary>Told (by push, and by WhatsApp if they opted in) when the balance drops under this. Null means no alert.</summary>
    public long? LowBalanceAlertCents { get; private set; }

    public Result SetAlerts(bool? notifyOnPurchase, long? lowBalanceAlertCents, bool clearLowBalanceAlert)
    {
        if (lowBalanceAlertCents is < 0 or > 500_000) return Result.Fail("wallet.alert_invalid", "The low balance alert is from R0 to R5000.");
        if (notifyOnPurchase is { } n) NotifyOnPurchase = n;
        if (clearLowBalanceAlert) LowBalanceAlertCents = null;
        else if (lowBalanceAlertCents is { } l) LowBalanceAlertCents = l;
        return Result.Ok();
    }
}
