using System.Text.Json;

namespace TillKnowIT.Security;

/// <summary>
/// Finds the person a request says is acting, so the server can check it is the person who signed in. In a set of fields (the query string, or one JSON object)
/// a "callerPersonId"-style field names the actor; when there is none, a plain "personId" does. A field that only names someone being acted upon
/// (the person given access, next to a caller) is therefore left alone.
/// </summary>
public static class PersonClaims
{
    private static readonly string[] Callers = ["callerpersonid", "parentpersonid", "adminpersonid", "agentpersonid", "ownerpersonid"];
    private const string Plain = "personid";

    /// <summary>The actor ids named in one set of fields.</summary>
    public static IReadOnlyList<Guid> From(IEnumerable<KeyValuePair<string, string?>> fields)
    {
        var list = fields.Where(f => Guid.TryParse(f.Value, out _)).Select(f => (Key: f.Key.ToLowerInvariant(), Id: Guid.Parse(f.Value!))).ToList();
        var callers = list.Where(f => Callers.Contains(f.Key)).Select(f => f.Id).ToList();
        return callers.Count > 0 ? callers : list.Where(f => f.Key == Plain).Select(f => f.Id).ToList();
    }

    /// <summary>The actor ids in a JSON body: the top-level object and the objects directly inside it.</summary>
    public static IReadOnlyList<Guid> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var found = new List<Guid>();
            Collect(doc.RootElement, found, depth: 0);
            return found;
        }
        catch (JsonException) { return []; }
    }

    private static void Collect(JsonElement element, List<Guid> found, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object || depth > 1) return;
        found.AddRange(From(element.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => new KeyValuePair<string, string?>(p.Name, p.Value.GetString()))));
        foreach (var p in element.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object)) Collect(p.Value, found, depth + 1);
    }

    /// <summary>The till ids a request names ("deviceId" in the query, route or JSON body), so a till's calls can be checked against its own token.</summary>
    public static IReadOnlyList<Guid> DevicesFrom(IEnumerable<KeyValuePair<string, string?>> fields) =>
        fields.Where(f => f.Key.Equals("deviceId", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(f.Value, out _)).Select(f => Guid.Parse(f.Value!)).ToList();

    public static IReadOnlyList<Guid> DevicesFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var found = new List<Guid>();
            CollectDevices(doc.RootElement, found, 0);
            return found;
        }
        catch (JsonException) { return []; }
    }

    private static void CollectDevices(JsonElement element, List<Guid> found, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object || depth > 1) return;
        found.AddRange(DevicesFrom(element.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => new KeyValuePair<string, string?>(p.Name, p.Value.GetString()))));
        foreach (var p in element.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object)) CollectDevices(p.Value, found, depth + 1);
    }
}
