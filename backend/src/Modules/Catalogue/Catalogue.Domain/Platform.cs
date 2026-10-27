using TillKnowIT.SharedKernel;

namespace Catalogue.Domain;

/// <summary>One table for all lookups (categories, units, payment_methods, vat_rates, adjustment_reasons, document_types, languages).</summary>
public sealed partial class Lookup : Entity
{
    public static readonly string[] Types = ["categories", "units", "payment_methods", "vat_rates", "adjustment_reasons", "document_types", "languages", "municipalities"];

    /// <summary>The six launch languages and the five added in Phase 5.</summary>
    public static readonly string[] LanguageCodes = ["en", "zu", "xh", "st", "tn", "af", "nso", "ts", "ve", "nr", "ss"];

    private Lookup() { }

    public Lookup(string type, string code, Dictionary<string, string> names, int sortOrder)
    {
        Type = type;
        Code = code;
        Names = names;
        SortOrder = sortOrder;
    }

    public static Result<Lookup> Create(string type, string code, Dictionary<string, string> names, int sortOrder)
    {
        if (!Types.Contains(type)) return Result<Lookup>.Fail("lookup.type_invalid", $"Lookup types are: {string.Join(", ", Types)}.");
        if (!CodePattern().IsMatch(code ?? "")) return Result<Lookup>.Fail("lookup.code_invalid", "A code is lowercase letters, digits and underscores, up to 50 long.");
        var checkedNames = CheckNames(names);
        if (!checkedNames.IsSuccess) return Result<Lookup>.Fail(checkedNames.Error!.Code, checkedNames.Error.Message);
        if (!checkedNames.Value!.TryGetValue("en", out var en) || string.IsNullOrWhiteSpace(en))
            return Result<Lookup>.Fail("lookup.english_required", "A new lookup needs an English name.");
        return Result<Lookup>.Ok(new Lookup(type, code!, checkedNames.Value!, sortOrder));
    }

    private static Result<Dictionary<string, string>> CheckNames(Dictionary<string, string> names)
    {
        var clean = new Dictionary<string, string>();
        foreach (var (language, name) in names)
        {
            if (!LanguageCodes.Contains(language)) return Result<Dictionary<string, string>>.Fail("lookup.language_invalid", $"Unknown language '{language}'.");
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) return Result<Dictionary<string, string>>.Fail("lookup.name_invalid", "A name is up to 200 characters.");
            clean[language] = name.Trim();
        }
        return Result<Dictionary<string, string>>.Ok(clean);
    }

    /// <summary>Adds or replaces translations. The English name can be changed but never removed.</summary>
    public Result Translate(Dictionary<string, string> names)
    {
        var checkedNames = CheckNames(names);
        if (!checkedNames.IsSuccess) return Result.Fail(checkedNames.Error!.Code, checkedNames.Error.Message);
        var merged = new Dictionary<string, string>(Names);
        foreach (var (language, name) in checkedNames.Value!) merged[language] = name;
        Names = merged;
        return Result.Ok();
    }

    public void SetActive(bool active) => IsActive = active;

    public void SetOrder(int sortOrder) => SortOrder = sortOrder;

    public string Type { get; private set; } = default!;
    public string Code { get; private set; } = default!;
    public Dictionary<string, string> Names { get; private set; } = [];
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9_]{1,50}$")]
    private static partial System.Text.RegularExpressions.Regex CodePattern();
}

public sealed class FeatureFlag : VersionedEntity
{
    private FeatureFlag() { }

    public FeatureFlag(string key, bool isEnabled)
    {
        Key = key;
        IsEnabled = isEnabled;
    }

    public string Key { get; private set; } = default!;
    public bool IsEnabled { get; private set; }
}

public sealed class AuditLogEntry : Entity
{
    private AuditLogEntry() { }

    public AuditLogEntry(string entityType, Guid entityId, string action, string? detailJson)
    {
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        DetailJson = detailJson;
    }

    public string EntityType { get; private set; } = default!;
    public Guid EntityId { get; private set; }
    public string Action { get; private set; } = default!;
    public string? DetailJson { get; private set; }
}
