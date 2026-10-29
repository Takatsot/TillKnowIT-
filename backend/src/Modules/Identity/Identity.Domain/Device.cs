using System.Security.Cryptography;
using System.Text;
using TillKnowIT.SharedKernel;

namespace Identity.Domain;

public enum DeviceStatus { Pending, Approved, Disabled }

public sealed class Device : VersionedEntity
{
    private Device() { }

    public Device(Guid shopId, string name)
    {
        ShopId = shopId;
        Name = name;
    }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = default!;
    public DeviceStatus Status { get; private set; } = DeviceStatus.Pending;
    public DateTimeOffset? LastSeenAt { get; private set; }
    public bool WipeRequested { get; private set; }

    /// <summary>Only a hash of the one-time code the owner gives the till when setting it up. Once the till has used it, the till proves itself with a signed token instead.</summary>
    public string? EnrolmentCodeHash { get; private set; }
    public DateTimeOffset? EnrolledAt { get; private set; }

    public bool IsEnrolled => EnrolledAt is not null;

    /// <summary>Goes up each time the owner resets the till's credential; the till's token names the version it was issued under, so an older token no longer checks in.</summary>
    public int CredentialVersion { get; private set; }

    public string TokenSubject => $"device:{Id}:{CredentialVersion}";

    /// <summary>The till lost its credential (or was handed over): the old token stops working, a new setup code is made, and the owner approves the till again.</summary>
    public Result<string> ResetCredential()
    {
        if (Status == DeviceStatus.Disabled) return Result<string>.Fail("device.disabled", "This till was switched off. Add it again.");
        CredentialVersion++;
        Status = DeviceStatus.Pending;
        var code = NewEnrolmentCode();
        Touch();
        return Result<string>.Ok(code);
    }

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Makes the one-time setup code (shown to the owner once) and keeps its hash.</summary>
    public string NewEnrolmentCode()
    {
        var code = new string(Enumerable.Range(0, 10).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());
        EnrolmentCodeHash = HashCode(code);
        EnrolledAt = null;
        return code;
    }

    public static string HashCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "")))).ToLowerInvariant();

    /// <summary>The till uses its setup code. It works once; after that only the till's signed token gets in.</summary>
    public Result Enrol(string? code, DateTimeOffset now)
    {
        if (IsEnrolled || EnrolmentCodeHash is null || string.IsNullOrWhiteSpace(code)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashCode(code)), Encoding.ASCII.GetBytes(EnrolmentCodeHash)))
            return Result.Fail("device.enrolment_required", "Enter the setup code the owner was shown when this till was added.");
        EnrolledAt = now;
        Touch();
        return Result.Ok();
    }

    /// <summary>The owner approves a pending device. A switched-off device must be registered again.</summary>
    public Result Approve()
    {
        if (Status == DeviceStatus.Disabled)
            return Result.Fail("device.disabled", "This device was switched off. Register it again.");
        Status = DeviceStatus.Approved;
        Touch();
        return Result.Ok();
    }

    /// <summary>Switch the device off. The till clears its local data the next time it starts and checks in.</summary>
    public void Wipe()
    {
        if (Status == DeviceStatus.Disabled && WipeRequested) return;
        Status = DeviceStatus.Disabled;
        WipeRequested = true;
        Touch();
    }

    public void ConfirmWiped()
    {
        if (!WipeRequested) return;
        WipeRequested = false;
        Touch();
    }

    public void RecordSeen(DateTimeOffset now)
    {
        LastSeenAt = now;
        Touch();
    }
}
