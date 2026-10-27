using System.Security.Cryptography;
using System.Text;

namespace TillKnowIT.Security;

/// <summary>
/// Signed callbacks: the header is "t=unix_seconds,v1=hex" where v1 is HMAC-SHA256 of "t.body" under the shared secret.
/// A callback is refused when the signature is wrong or the time is more than five minutes from ours (replays).
/// </summary>
public static class WebhookSignature
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, DateTimeOffset at, string body)
    {
        var t = at.ToUnixTimeSeconds();
        return $"t={t},v1={Hmac(secret, $"{t}.{body}")}";
    }

    public static bool Verify(string secret, string? header, string body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header)) return false;
        string? t = null, v1 = null;
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0].Trim() == "t") t = kv[1].Trim();
            else if (kv[0].Trim() == "v1") v1 = kv[1].Trim();
        }
        if (t is null || v1 is null || !long.TryParse(t, out var seconds)) return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > Tolerance) return false;

        var expected = Encoding.ASCII.GetBytes(Hmac(secret, $"{seconds}.{body}"));
        var given = Encoding.ASCII.GetBytes(v1.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private static string Hmac(string secret, string text) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
