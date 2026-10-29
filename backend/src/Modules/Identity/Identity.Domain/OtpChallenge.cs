using System.Security.Cryptography;
using System.Text;
using TillKnowIT.SharedKernel;

namespace Identity.Domain;

/// <summary>
/// A one-time code sent to a person's phone so they can sign in. Only a hash of the code is kept. A code works once, for ten minutes, and five wrong tries
/// use it up, so it cannot be guessed.
/// </summary>
public sealed class OtpChallenge : Entity
{
    public const int MaxAttempts = 5;
    public const int CodeDigits = 6;
    public static readonly TimeSpan Life = TimeSpan.FromMinutes(10);

    private OtpChallenge() { }

    public Guid PersonId { get; private set; }
    public string PhoneKey { get; private set; } = default!;
    public string CodeHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    public static (OtpChallenge Challenge, string Code) Issue(Guid personId, string phoneKey, DateTimeOffset now)
    {
        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, CodeDigits)).ToString().PadLeft(CodeDigits, '0');
        var c = new OtpChallenge { PersonId = personId, PhoneKey = phoneKey, ExpiresAt = now + Life };
        c.StampFromDevice(Guid.CreateVersion7(), now, personId, null);
        c.CodeHash = Hash(c.Id, code);
        return (c, code);
    }

    public static string Hash(Guid id, string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:N}:{code.Trim()}"))).ToLowerInvariant();

    public bool IsOpen(DateTimeOffset now) => UsedAt is null && Attempts < MaxAttempts && now < ExpiresAt;

    /// <summary>Checks a code. A wrong one counts as a try; the right one is used up at once.</summary>
    public Result Check(string? code, DateTimeOffset now)
    {
        if (!IsOpen(now)) return Result.Fail("otp.invalid", "That code is not right or has expired. Ask for a new one.");
        if (string.IsNullOrWhiteSpace(code) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(Id, code)), Encoding.ASCII.GetBytes(CodeHash)))
        {
            Attempts++;
            return Result.Fail("otp.invalid", "That code is not right or has expired. Ask for a new one.");
        }
        UsedAt = now;
        return Result.Ok();
    }
}
