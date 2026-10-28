namespace Marketplace.Contracts;

/// <summary>BuyingGroupId limits the deal to the members of one buying group.</summary>
public sealed record PostGroupDealRequest(Guid CallerPersonId, Guid SupplierProductId, long BulkPriceCents, int MinTotalCases, DateTimeOffset ClosesAt, string AreaCode, Guid? BuyingGroupId = null);

public sealed record JoinGroupDealRequest(Guid CallerPersonId, int Cases);

/// <summary>
/// What a shop sees of a group: the total and its own quantity, never the other shops. Shops is only filled in for the supplier
/// who posted it, and is a count, not names.
/// </summary>
public sealed record GroupDealResponse(
    Guid Id, Guid SupplierId, string SupplierName, Guid SupplierProductId, string ProductName, int UnitsPerCase, long BulkPriceCents, long CasePriceCents,
    int MinTotalCases, int TotalCases, int ProgressPercent, DateTimeOffset ClosesAt, string AreaCode, string Status, int? YourCases, int? Shops, Guid? BuyingGroupId = null);

public sealed record GroupClosing(Guid GroupDealId, string Outcome, int Orders);

public sealed record CreateBuyingGroupRequest(Guid CallerPersonId, Guid ShopId, string Name, string Kind);

public sealed record BuyingGroupResponse(Guid Id, string Name, string Kind, Guid AdminShopId, int Members, bool YouAreAdmin);

/// <summary>Code is shown here once. Share it with the shop that should join; it works once and for seven days.</summary>
public sealed record InviteResponse(Guid GroupId, string Code, DateTimeOffset ExpiresAt);

public sealed record AcceptInviteRequest(Guid CallerPersonId, string Code);

public sealed record MemberResponse(Guid ShopId, string ShopName, DateTimeOffset JoinedAt, int? CasesOnOpenDeals);

/// <summary>
/// A group's shared order history: every deal made for the group, the total, and each member's own quantity.
/// Members is only filled in for the group admin.
/// </summary>
public sealed record GroupHistoryResponse(Guid GroupId, string Name, List<GroupDealResponse> Deals, List<MemberResponse>? Members);
