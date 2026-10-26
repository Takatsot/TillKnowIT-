using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TillKnowIT.Outbox;

public static class NamesComparer
{
    private static readonly ValueComparer<Dictionary<string, string>> Comparer = new(
        (a, b) => a != null && b != null && a.Count == b.Count && !a.Except(b).Any(),
        d => d.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
        d => new Dictionary<string, string>(d));

    private static string ToJson(Dictionary<string, string> names) => JsonSerializer.Serialize(names);

    private static Dictionary<string, string> FromJson(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    /// <summary>Stores per-language names as JSON text and compares them by content, so seed data and change tracking behave.</summary>
    public static PropertyBuilder<Dictionary<string, string>> AsLanguageNames(this PropertyBuilder<Dictionary<string, string>> property)
    {
        property.HasConversion(v => ToJson(v), s => FromJson(s), Comparer);
        property.HasColumnType("nvarchar(max)");
        return property;
    }
}
