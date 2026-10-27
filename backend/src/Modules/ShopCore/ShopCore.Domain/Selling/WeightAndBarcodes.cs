using System.Text.Json;
using TillKnowIT.SharedKernel;

namespace ShopCore.Domain.Selling;

/// <summary>B4-1: selling by weight. A weighed item is priced per kilogram and sold in grams.</summary>
public static class WeightPricing
{
    public const int MaxGrams = 50_000;

    /// <summary>What a weighed line costs: the net weight (after tare) at the price per kilogram, rounded half up to the cent.</summary>
    public static Result<long> LineTotal(long pricePerKgCents, int grossGrams, int tareGrams = 0)
    {
        if (pricePerKgCents < 0) return Result<long>.Fail("weight.price_invalid", "The price per kilogram cannot be negative.");
        if (grossGrams <= 0 || grossGrams > MaxGrams) return Result<long>.Fail("weight.weight_invalid", "The weight must be more than zero and at most 50 kg.");
        if (tareGrams < 0 || tareGrams >= grossGrams) return Result<long>.Fail("weight.tare_invalid", "The tare must be less than the weight on the scale.");
        var net = grossGrams - tareGrams;
        return Result<long>.Ok((pricePerKgCents * net + 500) / 1000);
    }
}

public enum EmbeddedValueType { Weight, Price }

/// <summary>
/// B4-2: how this shop's labelling scale writes a price or weight into a barcode, read from the pack settings. Prefix, then the item code,
/// then the value, then the EAN-13 check digit; so a 13-digit code with prefix "2", 6 item digits and 5 value digits is "2" + item + value + check.
/// </summary>
public sealed record EmbeddedBarcodeRules(string Prefix, int ItemDigits, int ValueDigits, EmbeddedValueType ValueType, int Decimals)
{
    public static Result<EmbeddedBarcodeRules> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Result<EmbeddedBarcodeRules>.Fail("barcode.rules_missing", "This shop has no price-embedded barcode rules.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var prefix = r.GetProperty("prefix").GetString() ?? "";
            var item = r.GetProperty("itemDigits").GetInt32();
            var value = r.GetProperty("valueDigits").GetInt32();
            var type = r.GetProperty("valueType").GetString()?.ToLowerInvariant() == "price" ? EmbeddedValueType.Price : EmbeddedValueType.Weight;
            var decimals = r.TryGetProperty("decimals", out var d) ? d.GetInt32() : (type == EmbeddedValueType.Weight ? 3 : 2);
            var rules = new EmbeddedBarcodeRules(prefix, item, value, type, decimals);
            return rules.IsValid() ? Result<EmbeddedBarcodeRules>.Ok(rules) : Result<EmbeddedBarcodeRules>.Fail("barcode.rules_invalid", "The barcode rules do not fit a 13-digit code.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Result<EmbeddedBarcodeRules>.Fail("barcode.rules_invalid", "The barcode rules are not valid.");
        }
    }

    /// <summary>Prefix, item, value and one check digit must make exactly 13 digits.</summary>
    public bool IsValid() =>
        Prefix.Length is > 0 and <= 3 && Prefix.All(char.IsDigit) && ItemDigits > 0 && ValueDigits > 0 && Decimals is >= 0 and <= 4
        && Prefix.Length + ItemDigits + ValueDigits + 1 == 13;
}

/// <summary>What was read off a label: the item, and either the weight in grams or the price in cents.</summary>
public sealed record EmbeddedBarcode(string ItemCode, int? WeightGrams, long? PriceCents);

public static class EmbeddedBarcodes
{
    /// <summary>The EAN-13 check digit for the first 12 digits.</summary>
    public static int CheckDigit(string first12)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (first12[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    public static bool HasValidCheckDigit(string code) =>
        code.Length == 13 && code.All(char.IsDigit) && CheckDigit(code[..12]) == code[12] - '0';

    /// <summary>Null when the code is not one of this shop's scale labels (wrong prefix, length, or check digit), so an ordinary barcode is left for the normal lookup.</summary>
    public static EmbeddedBarcode? Parse(string? code, EmbeddedBarcodeRules rules)
    {
        if (code is null || !HasValidCheckDigit(code) || !code.StartsWith(rules.Prefix, StringComparison.Ordinal)) return null;
        var start = rules.Prefix.Length;
        var item = code.Substring(start, rules.ItemDigits);
        var value = long.Parse(code.Substring(start + rules.ItemDigits, rules.ValueDigits));
        if (value <= 0) return null;

        // Decimals say how many of the value digits are fractional: weight 3 decimals is kilograms to the gram; price 2 decimals is rand and cents.
        if (rules.ValueType == EmbeddedValueType.Weight)
        {
            var grams = rules.Decimals switch { 0 => value * 1000, 1 => value * 100, 2 => value * 10, 3 => value, _ => value / 10 };
            return grams is > 0 and <= WeightPricing.MaxGrams ? new EmbeddedBarcode(item, (int)grams, null) : null;
        }
        var cents = rules.Decimals switch { 0 => value * 100, 1 => value * 10, 2 => value, 3 => (value + 5) / 10, _ => (value + 50) / 100 };
        return new EmbeddedBarcode(item, null, cents);
    }

    /// <summary>Builds a label code, for tests and for printing scales that are set up from here.</summary>
    public static string Build(EmbeddedBarcodeRules rules, string itemCode, long value)
    {
        var body = rules.Prefix + itemCode.PadLeft(rules.ItemDigits, '0')[^rules.ItemDigits..] + value.ToString().PadLeft(rules.ValueDigits, '0')[^rules.ValueDigits..];
        return body + CheckDigit(body);
    }
}
