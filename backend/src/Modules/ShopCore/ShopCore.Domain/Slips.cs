using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ShopCore.Contracts;
using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public enum SlipChannel { Qr, Whatsapp, Sms, Email }

public static class SlipChannels
{
    public static bool TryParse(string? value, out SlipChannel channel)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "qr": channel = SlipChannel.Qr; return true;
            case "whatsapp": channel = SlipChannel.Whatsapp; return true;
            case "sms": channel = SlipChannel.Sms; return true;
            case "email": channel = SlipChannel.Email; return true;
            default: channel = default; return false;
        }
    }
}

/// <summary>A slip given only because the customer asked for one. QR is free; the others are messages that cost money.</summary>
public sealed class SlipRequest : Entity
{
    /// <summary>At most this many slips per sale, so the button cannot be used to send someone a stream of messages.</summary>
    public const int MaxPerSale = 3;

    private SlipRequest() { }

    public static SlipRequest Request(Guid shopId, Guid saleId, Guid staffId, Guid deviceId, SlipChannel channel, string? destination,
        string language, IReadOnlyDictionary<string, string> values, DateTimeOffset now)
    {
        var slip = new SlipRequest
        {
            ShopId = shopId, SaleId = saleId, Channel = channel, Destination = destination,
            Status = channel == SlipChannel.Qr ? "shown" : "queued",
        };
        slip.StampFromDevice(Guid.CreateVersion7(), now, staffId, deviceId);

        // QR slips need no message. The others hand the words to Messaging through the outbox, in the same save.
        if (channel != SlipChannel.Qr)
            slip.Raise(new SlipRequested(slip.Id, shopId, saleId, channel.ToString().ToLowerInvariant(), destination!, language,
                new Dictionary<string, string>(values)));
        return slip;
    }

    public Guid ShopId { get; private set; }
    public Guid SaleId { get; private set; }
    public SlipChannel Channel { get; private set; }
    public string? Destination { get; private set; }
    public string Status { get; private set; } = "queued";
}

/// <summary>A link that shows one slip and nothing else: the sale id plus a signature, so ids cannot be guessed.</summary>
public static class SlipToken
{
    private const int SignatureBytes = 16;

    public static string Create(Guid saleId, byte[] key)
    {
        var id = saleId.ToByteArray();
        var signature = HMACSHA256.HashData(key, id)[..SignatureBytes];
        return Base64Url([.. id, .. signature]);
    }

    /// <summary>False for anything that is not a token we made, however close it looks.</summary>
    public static bool TryRead(string? token, byte[] key, out Guid saleId)
    {
        saleId = Guid.Empty;
        if (token is null || token.Length != 43) return false;
        byte[] bytes;
        try { bytes = FromBase64Url(token); }
        catch (FormatException) { return false; }
        if (bytes.Length != 16 + SignatureBytes) return false;

        var expected = HMACSHA256.HashData(key, bytes[..16])[..SignatureBytes];
        if (!CryptographicOperations.FixedTimeEquals(expected, bytes[16..])) return false;
        saleId = new Guid(bytes[..16]);
        return true;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

public sealed record SlipLine(string Name, int Quantity, long PriceCents);

public static class SlipFormatter
{
    private static string Rand(long cents) => "R" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>"2 x Bread  R36.00", one line per item.</summary>
    public static string Lines(IEnumerable<SlipLine> lines) =>
        string.Join("\n", lines.Select(l => $"{l.Quantity} x {l.Name}  {Rand(l.PriceCents * l.Quantity)}"));

    public static string Total(long cents) => Rand(cents);

    /// <summary>The date as the shop saw it (South African time), for example "26 Oct 2026 12:04".</summary>
    public static string Date(DateTimeOffset soldAt) =>
        soldAt.ToOffset(BusinessDay.Offset).ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    public static Dictionary<string, string> Values(string shopName, IEnumerable<SlipLine> lines, long totalCents, DateTimeOffset soldAt, string url) => new()
    {
        ["shop"] = shopName,
        ["lines"] = Lines(lines),
        ["total"] = Total(totalCents),
        ["date"] = Date(soldAt),
        ["url"] = url,
    };
}
