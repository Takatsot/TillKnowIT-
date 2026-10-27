using TillKnowIT.SharedKernel;

namespace Identity.Infrastructure.Persistence;

/// <summary>Roles and permissions shipped with every environment. Edit through new migrations.</summary>
internal static class IdentitySeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static Guid RoleId(string code) => DeterministicGuid.From($"identity.role:{code}");

    private static readonly string[] ShopPermissions =
    [
        "sale.create", "sale.cancel", "sale.override_expiry", "slip.send",
        "product.edit", "stock.receive", "stock.count", "credit.manage", "cashup.do",
        "report.view", "compliance.manage", "vas.sell", "credit.payment",
    ];

    public static readonly (string Code, string En, int Sort, string[] Permissions)[] Roles =
    [
        ("owner", "Owner", 1, [.. ShopPermissions, "staff.manage", "device.manage", "shop.settings"]),
        ("manager", "Manager", 2, ShopPermissions),
        ("cashier", "Cashier", 3, ["sale.create", "slip.send", "cashup.do", "vas.sell", "credit.payment"]),
        ("field_agent", "Field agent", 4, ["shop.setup", "shop.verify", "staff.manage", "device.manage"]),
        ("platform_admin", "Platform admin", 5, ["catalogue.approve", "lookup.manage", "shop.verify", "user.manage"]),
    ];

    public static object[] RoleRows() => Roles.Select(r => (object)new
    {
        Id = RoleId(r.Code),
        r.Code,
        Names = new Dictionary<string, string> { ["en"] = r.En },
        IsActive = true,
        SortOrder = r.Sort,
        CreatedAt = SeededAt,
    }).ToArray();

    public static object[] PermissionRows() => Roles
        .SelectMany(r => r.Permissions.Select(p => (object)new { RoleId = RoleId(r.Code), PermissionCode = p }))
        .ToArray();
}
