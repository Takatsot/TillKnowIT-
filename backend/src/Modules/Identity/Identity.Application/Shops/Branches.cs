using Billing.Contracts;
using Identity.Contracts;
using Identity.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Shops;

public interface IBranchStore
{
    Task<Organisation?> GetShop(Guid shopId, CancellationToken ct);
    Task<Membership?> MembershipAt(Guid personId, Guid organisationId, CancellationToken ct);
    Task<Guid> RoleId(string code, CancellationToken ct);
    Task Add(Organisation shop, Membership owner, CancellationToken ct);
    Task<List<Organisation>> BranchesOf(Guid headId, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public sealed record AddBranch(Guid HeadShopId, AddBranchRequest Request) : ICommand<BranchResponse>;
public sealed record ListBranches(Guid ShopId, Guid CallerPersonId) : IQuery<List<BranchResponse>>;
public sealed record GrantAllBranches(Guid HeadShopId, GrantAllBranchesRequest Request) : ICommand<bool>;

/// <summary>
/// G1-1, G1-4: several shops under one owner. A branch is its own shop with its own tills and staff; the owner sees all of them,
/// and a manager only sees the branch they work at unless the owner gives them every branch.
/// </summary>
internal sealed class BranchHandlers(IBranchStore store, IOrganisationAccess access, IEntitlements entitlements, Catalogue.Contracts.ILookupChecker lookups)
    : ICommandHandler<AddBranch, BranchResponse>, IQueryHandler<ListBranches, List<BranchResponse>>, ICommandHandler<GrantAllBranches, bool>
{
    private static BranchResponse Respond(Organisation o, bool head) => new(o.Id, o.Name, head, o.Status.ToString().ToLowerInvariant(), o.MunicipalityCode);

    public async Task<Result<BranchResponse>> Handle(AddBranch c, CancellationToken ct)
    {
        var r = c.Request;
        var head = await store.GetShop(c.HeadShopId, ct);
        if (head is null || head.Type != OrganisationType.Shop) return Result<BranchResponse>.Fail("shop.not_found", "Shop not found.");
        if (!await access.Has(r.CallerPersonId, head.Id, "shop.settings", ct)) return Result<BranchResponse>.Fail("permission.denied", "Only the owner can add branches.");
        if (!await entitlements.Has(head.Id, "shop", PlanFeatures.MultiBranch, ct)) return Result<BranchResponse>.Fail("plan.feature_not_included", "Branches are not part of your plan.");

        string? municipality = null;
        if (!string.IsNullOrWhiteSpace(r.Area))
        {
            municipality = r.Area.Trim();
            if (!await lookups.IsActive("municipalities", municipality, ct)) return Result<BranchResponse>.Fail("shop.area_invalid", "That area is not one we know. Choose the municipality from the list.");
        }

        var branch = Organisation.CreateBranch(head, r.Name, municipality);
        if (!branch.IsSuccess) return Result<BranchResponse>.Fail(branch.Error!.Code, branch.Error.Message);

        // The owner works at the branch too, and from now on sees every branch.
        var owner = new Membership(r.CallerPersonId, branch.Value!.Id, await store.RoleId("owner", ct));
        await store.Add(branch.Value, owner, ct);
        (await store.MembershipAt(r.CallerPersonId, head.Id, ct))?.SetScope(MembershipScope.AllBranches);
        await store.Save(ct);
        return Result<BranchResponse>.Ok(Respond(branch.Value, false));
    }

    public async Task<Result<List<BranchResponse>>> Handle(ListBranches q, CancellationToken ct)
    {
        var shop = await store.GetShop(q.ShopId, ct);
        if (shop is null) return Result<List<BranchResponse>>.Fail("shop.not_found", "Shop not found.");
        var head = shop.ParentId is { } p ? await store.GetShop(p, ct) ?? shop : shop;
        if (!await access.Has(q.CallerPersonId, head.Id, "report.view", ct)) return Result<List<BranchResponse>>.Fail("permission.denied", "You are not allowed to see the branches.");

        var branches = (await store.BranchesOf(head.Id, ct)).OrderBy(b => b.Name).Select(b => Respond(b, false));
        return Result<List<BranchResponse>>.Ok([Respond(head, true), .. branches]);
    }

    /// <summary>Gives a person who works at the head shop access to every branch (a general manager). Branch staff stay at their own branch.</summary>
    public async Task<Result<bool>> Handle(GrantAllBranches c, CancellationToken ct)
    {
        var head = await store.GetShop(c.HeadShopId, ct);
        if (head is null || head.ParentId is not null) return Result<bool>.Fail("shop.not_found", "Head shop not found.");
        if (!await access.Has(c.Request.CallerPersonId, head.Id, "staff.manage", ct)) return Result<bool>.Fail("permission.denied", "Only the owner can give access to every branch.");
        var membership = await store.MembershipAt(c.Request.PersonId, head.Id, ct);
        if (membership is null) return Result<bool>.Fail("staff.unknown", "That person does not work at the head shop.");
        membership.SetScope(MembershipScope.AllBranches);
        await store.Save(ct);
        return Result<bool>.Ok(true);
    }
}
