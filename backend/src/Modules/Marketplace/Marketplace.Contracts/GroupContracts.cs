namespace Marketplace.Contracts;

public sealed record PostGroupDealRequest(Guid CallerPersonId, Guid SupplierProductId, long BulkPriceCents, int MinTotalCases, DateTimeOffset ClosesAt, string AreaCode);

public sealed record JoinGroupDealRequest(Guid CallerPersonId, int Cases);

/// <summary>
/// What a shop sees of a group: the total and its own quantity, never the other shops. Shops is only filled in for the supplier
/// who posted it, and is a count, not names.
/// </summary>
public sealed record GroupDealResponse(
    Guid Id, Guid SupplierId, string SupplierName, Guid SupplierProductId, string ProductName, int UnitsPerCase, long BulkPriceCents, long CasePriceCents,
    int MinTotalCases, int TotalCases, int ProgressPercent, DateTimeOffset ClosesAt, string AreaCode, string Status, int? YourCases, int? Shops);

public sealed record GroupClosing(Guid GroupDealId, string Outcome, int Orders);
