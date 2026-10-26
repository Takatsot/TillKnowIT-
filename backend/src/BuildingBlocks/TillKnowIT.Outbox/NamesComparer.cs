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

    /// <summary>Stores per-language names as jsonb and compares them by content, so seed data and change tracking behave.</summary>
    public static PropertyBuilder<Dictionary<string, string>> AsLanguageNames(this PropertyBuilder<Dictionary<string, string>> property)
    {
        property.HasColumnType("jsonb");
        property.Metadata.SetValueComparer(Comparer);
        return property;
    }
}
