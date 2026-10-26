using TillKnowIT.SharedKernel;

namespace Catalogue.Domain;

/// <summary>One table for all lookups (categories, units, payment_methods, vat_rates, adjustment_reasons, document_types, languages).</summary>
public sealed class Lookup : Entity
{
    private Lookup() { }

    public Lookup(string type, string code, Dictionary<string, string> names, int sortOrder)
    {
        Type = type;
        Code = code;
        Names = names;
        SortOrder = sortOrder;
    }

    public string Type { get; private set; } = default!;
    public string Code { get; private set; } = default!;
    public Dictionary<string, string> Names { get; private set; } = [];
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }
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
