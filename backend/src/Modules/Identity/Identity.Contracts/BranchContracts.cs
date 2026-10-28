namespace Identity.Contracts;

public sealed record AddBranchRequest(Guid CallerPersonId, string Name, string? Area);

public sealed record GrantAllBranchesRequest(Guid CallerPersonId, Guid PersonId);

public sealed record BranchResponse(Guid ShopId, string Name, bool IsHead, string Status, string? MunicipalityCode);

/// <summary>How other modules find the branches of a business without reading Identity's tables (combined reports, stock transfers, accounting).</summary>
public interface IBranchDirectory
{
    /// <summary>The head shop for any shop: the shop's own id if it is a head shop or has no branches.</summary>
    Task<Guid> HeadOf(Guid shopId, CancellationToken ct);

    /// <summary>The head shop and all its branches, head first.</summary>
    Task<List<BranchResponse>> Group(Guid shopId, CancellationToken ct);
}
