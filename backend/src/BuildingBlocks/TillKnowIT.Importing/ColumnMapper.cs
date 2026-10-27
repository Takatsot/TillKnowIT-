using System.Globalization;
using System.Text.RegularExpressions;

namespace TillKnowIT.Importing;

/// <summary>Which sheet column holds each field, by header text. A saved mapping is just this dictionary, kept per supplier.</summary>
public sealed record ColumnMapping(Dictionary<string, string> FieldToHeader);

public static partial class ColumnMapper
{
    public const string Barcode = "barcode";
    public const string Name = "name";
    public const string Quantity = "quantity";
    public const string UnitsPerCase = "units_per_case";
    public const string CaseCost = "case_cost";
    public const string UnitCost = "unit_cost";
    public const string Price = "price";

    public static readonly string[] Fields = [Barcode, Name, Quantity, UnitsPerCase, CaseCost, UnitCost, Price];

    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        [Barcode] = ["barcode", "ean", "upc", "gtin", "bar code", "item code", "product code", "sku"],
        [Name] = ["name", "product", "description", "item", "product name", "item description", "desc"],
        [Quantity] = ["quantity", "qty", "units", "cases", "order qty", "qty ordered", "amount", "count"],
        [UnitsPerCase] = ["units per case", "case size", "pack size", "pack", "units/case", "per case", "case qty", "units per pack"],
        [CaseCost] = ["case cost", "case price", "cost per case", "pack price", "price per case"],
        [UnitCost] = ["unit cost", "cost", "cost price", "unit price", "price each", "cost per unit", "each"],
        [Price] = ["selling price", "retail price", "rrp", "sell price", "retail"],
    };

    /// <summary>Best guess of the mapping from the header row. A field with no obvious column is left out for the owner to choose.</summary>
    public static ColumnMapping Suggest(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, string>();
        var taken = new HashSet<string>();
        foreach (var field in Fields)
        {
            // Exact match on a synonym first, then a header that merely contains one.
            var match = headers.FirstOrDefault(h => !taken.Contains(h) && Synonyms[field].Contains(Normalise(h)))
                        ?? headers.FirstOrDefault(h => !taken.Contains(h) && Synonyms[field].Any(s => s.Length > 3 && Normalise(h).Contains(s)));
            if (match is not null) { map[field] = match; taken.Add(match); }
        }
        return new ColumnMapping(map);
    }

    /// <summary>A saved mapping only counts if every header it names is still in this file; otherwise the file changed and we guess again.</summary>
    public static ColumnMapping Reconcile(ColumnMapping? saved, IReadOnlyList<string> headers)
    {
        if (saved is null || saved.FieldToHeader.Count == 0) return Suggest(headers);
        var present = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return saved.FieldToHeader.Values.All(present.Contains) ? saved : Suggest(headers);
    }

    public static string? Cell(Sheet sheet, IReadOnlyList<string> row, ColumnMapping mapping, string field)
    {
        if (!mapping.FieldToHeader.TryGetValue(field, out var header)) return null;
        var index = -1;
        for (var i = 0; i < sheet.Headers.Count; i++)
            if (string.Equals(sheet.Headers[i], header, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
        return index < 0 ? null : row[index];
    }

    private static string Normalise(string header) => Spaces().Replace(header.Trim().ToLowerInvariant().Replace('_', ' '), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // ---- number reading ----

    /// <summary>"R 1 234,50", "1,234.50", "18.5" and "18" are all read as cents. Anything else (text, negatives, three decimals) is null.</summary>
    public static long? ParseMoneyCents(string? text)
    {
        var d = ParseDecimal(text);
        return d is null or < 0 || d * 100 != decimal.Truncate(d.Value * 100) ? null : (long)(d.Value * 100);
    }

    public static int? ParseWholeNumber(string? text)
    {
        var d = ParseDecimal(text);
        return d is null or < 0 || d != decimal.Truncate(d.Value) || d > int.MaxValue ? null : (int)d.Value;
    }

    private static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = Currency().Replace(text, "").Replace(" ", "").Replace(" ", "");
        // Whichever of , and . comes last is the decimal mark; the other is a thousands separator.
        var lastComma = t.LastIndexOf(',');
        var lastDot = t.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0) t = lastComma > lastDot ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else if (lastComma >= 0) t = t.Count(c => c == ',') == 1 && t.Length - lastComma - 1 <= 2 ? t.Replace(',', '.') : t.Replace(",", "");
        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    [GeneratedRegex(@"^(R|ZAR)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Currency();
}
