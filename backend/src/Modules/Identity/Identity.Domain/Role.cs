using TillKnowIT.SharedKernel;

namespace Identity.Domain;

/// <summary>Lookup shape: code, names per language, is_active, sort_order. Permissions are data, e.g. sale.cancel.</summary>
public sealed class Role : Entity
{
    private Role() { }

    public Role(string code, Dictionary<string, string> names, int sortOrder)
    {
        Code = code;
        Names = names;
        SortOrder = sortOrder;
    }

    public string Code { get; private set; } = default!;
    public Dictionary<string, string> Names { get; private set; } = [];
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }
    public List<RolePermission> Permissions { get; private set; } = [];
}

public sealed class RolePermission
{
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = default!;
}
