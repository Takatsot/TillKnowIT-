using System.Security.Cryptography;
using System.Text;

namespace TillKnowIT.Security;

/// <summary>
/// A short text token that proves the server issued it: "payload.mac", both base64url, the mac an HMAC-SHA256 of the payload.
/// Used for rotating QR codes and signed daily allowances. Nothing secret goes in the payload: it can be read, but not changed.
/// </summary>
public static class SignedToken
{
    public static string Sign(string key, string payload) => $"{Base64Url(Encoding.UTF8.GetBytes(payload))}.{Base64Url(Mac(key, payload))}";

    /// <summary>The payload if the token was signed under this key; otherwise null. A wrong key, a changed payload or a garbled token all give null.</summary>
    public static string? Verify(string key, string? token)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;
        try
        {
            var payload = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
            return CryptographicOperations.FixedTimeEquals(Mac(key, payload), FromBase64Url(parts[1])) ? payload : null;
        }
        catch (FormatException) { return null; }
    }

    private static byte[] Mac(string key, string payload) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

/// <summary>A signed token that stops working after a time: for QR codes, which must expire within 60 seconds.</summary>
public static class ExpiringToken
{
    /// <summary>Subject is anything without a '|' in it. Returns the token and when it stops working.</summary>
    public static (string Token, DateTimeOffset ExpiresAt) Issue(string key, string subject, DateTimeOffset now, TimeSpan lifetime)
    {
        var expires = now + lifetime;
        return (SignedToken.Sign(key, $"{subject}|{expires.ToUnixTimeSeconds()}"), expires);
    }

    /// <summary>The subject if the token is genuine and has not expired (a few seconds of clock difference are allowed); otherwise null.</summary>
    public static string? Verify(string key, string? token, DateTimeOffset now)
    {
        var payload = SignedToken.Verify(key, token);
        if (payload is null) return null;
        var cut = payload.LastIndexOf('|');
        if (cut < 0 || !long.TryParse(payload[(cut + 1)..], out var seconds)) return null;
        return now <= DateTimeOffset.FromUnixTimeSeconds(seconds).AddSeconds(5) ? payload[..cut] : null;
    }
}
