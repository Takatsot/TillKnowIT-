using TillKnowIT.SharedKernel;

namespace Catalogue.Infrastructure.Persistence;

/// <summary>
/// Starting lookups for the spaza pack. Only English names are seeded; the other five languages are added
/// by translators through the admin screens or a later migration, so no text is machine-guessed.
/// </summary>
internal static class CatalogueSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly (string Type, string[] Rows)[] Lookups =
    [
        ("categories", ["bread_bakery|Bread and bakery", "dairy_eggs|Dairy and eggs", "drinks|Cold drinks", "snacks_sweets|Snacks and sweets",
            "groceries|Groceries and staples", "frozen|Frozen food", "toiletries|Toiletries", "household|Household and cleaning",
            "baby|Baby products", "tobacco|Tobacco", "other|Other"]),
        ("units", ["each|Each", "pack|Pack", "case|Case", "kg|Kilogram", "litre|Litre"]),
        ("payment_methods", ["cash|Cash", "card|Card", "qr|QR payment", "credit|Credit book"]),
        // The rate is encoded in the code, because lookups carry only code and names.
        ("vat_rates", ["vat_15|VAT 15%", "vat_0|Zero rated"]),
        ("adjustment_reasons", ["counted|Counted", "expired|Expired", "damaged|Damaged", "missing|Missing", "own_use|Own use"]),
        ("document_types", ["trading_licence|Business or trading licence", "health_certificate|Certificate of acceptability",
            "food_permit|Food handling permit", "fire_certificate|Fire certificate", "other|Other document"]),
        ("languages", ["en|English", "zu|isiZulu", "xh|isiXhosa", "st|Sesotho", "tn|Setswana", "af|Afrikaans"]),
    ];

    public static object[] Rows() => Lookups
        .SelectMany(l => l.Rows.Select((row, i) =>
        {
            var parts = row.Split('|');
            return (object)new
            {
                Id = DeterministicGuid.From($"platform.lookup:{l.Type}:{parts[0]}"),
                Type = l.Type,
                Code = parts[0],
                Names = new Dictionary<string, string> { ["en"] = parts[1] },
                IsActive = true,
                SortOrder = i + 1,
                CreatedAt = SeededAt,
            };
        }))
        .ToArray();
}
