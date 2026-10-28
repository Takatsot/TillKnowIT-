using Identity.Contracts;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Orders;

public interface IBuyingGroupStore
{
    void Add(BuyingGroup group);
    Task<BuyingGroup?> Get(Guid groupId, CancellationToken ct);
    Task<List<BuyingGroup>> ForShop(Guid shopId, CancellationToken ct);

    void Add(BuyingGroupMember member);
    Task<BuyingGroupMember?> Member(Guid groupId, Guid shopId, CancellationToken ct);
    Task<List<BuyingGroupMember>> Members(Guid groupId, CancellationToken ct);

    /// <summary>The groups this shop is a current member of.</summary>
    Task<List<Guid>> GroupIdsOf(Guid shopId, CancellationToken ct);

    void Add(BuyingGroupInvite invite);
    Task<BuyingGroupInvite?> InviteByHash(string hash, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public sealed record CreateBuyingGroup(CreateBuyingGroupRequest Request) : ICommand<BuyingGroupResponse>;
public sealed record InviteToGroup(Guid GroupId, Guid CallerPersonId) : ICommand<InviteResponse>;
public sealed record AcceptGroupInvite(Guid ShopId, AcceptInviteRequest Request) : ICommand<BuyingGroupResponse>;
public sealed record LeaveBuyingGroup(Guid GroupId, Guid ShopId, Guid CallerPersonId) : ICommand<Unit>;
public sealed record RemoveGroupMember(Guid GroupId, Guid ShopId, Guid CallerPersonId) : ICommand<Unit>;
public sealed record ListBuyingGroups(Guid ShopId, Guid CallerPersonId) : IQuery<List<BuyingGroupResponse>>;
public sealed record GetGroupHistory(Guid GroupId, Guid ShopId, Guid CallerPersonId) : IQuery<GroupHistoryResponse>;

internal sealed class BuyingGroupHandlers(IBuyingGroupStore store, IGroupDealStore deals, ISupplierStore suppliers, ISupplierProductStore products, IShopDirectory shops, IOrganisationAccess access, TimeProvider clock)
    : ICommandHandler<CreateBuyingGroup, BuyingGroupResponse>, ICommandHandler<InviteToGroup, InviteResponse>, ICommandHandler<AcceptGroupInvite, BuyingGroupResponse>,
      ICommandHandler<LeaveBuyingGroup, Unit>, ICommandHandler<RemoveGroupMember, Unit>, IQueryHandler<ListBuyingGroups, List<BuyingGroupResponse>>, IQueryHandler<GetGroupHistory, GroupHistoryResponse>
{
    private async Task<BuyingGroupResponse> Respond(BuyingGroup g, Guid? forShop, CancellationToken ct) =>
        new(g.Id, g.Name, g.Kind.ToString().ToLowerInvariant(), g.AdminShopId, (await store.Members(g.Id, ct)).Count(m => m.IsMember), forShop == g.AdminShopId);

    private Task<bool> IsAdmin(BuyingGroup g, Guid person, CancellationToken ct) => access.Has(person, g.AdminShopId, "shop.settings", ct);

    /// <summary>The owner of a shop starts a group. The shop is its admin and first member, and can invite others.</summary>
    public async Task<Result<BuyingGroupResponse>> Handle(CreateBuyingGroup c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await access.Has(r.CallerPersonId, r.ShopId, "shop.settings", ct)) return Result<BuyingGroupResponse>.Fail("permission.denied", "Only the shop owner can start a group.");
        var now = clock.GetUtcNow();
        var group = BuyingGroup.Create(r.Name, r.Kind, r.ShopId, r.CallerPersonId, now);
        if (!group.IsSuccess) return Result<BuyingGroupResponse>.Fail(group.Error!.Code, group.Error.Message);
        store.Add(group.Value!);
        store.Add(BuyingGroupMember.Join(group.Value!.Id, r.ShopId, r.CallerPersonId, now));
        await store.Save(ct);
        return Result<BuyingGroupResponse>.Ok(await Respond(group.Value, r.ShopId, ct));
    }

    public async Task<Result<InviteResponse>> Handle(InviteToGroup c, CancellationToken ct)
    {
        var group = await store.Get(c.GroupId, ct);
        if (group is null || !group.IsActive) return Result<InviteResponse>.Fail("buying_group.not_found", "Group not found.");
        if (!await IsAdmin(group, c.CallerPersonId, ct)) return Result<InviteResponse>.Fail("permission.denied", "Only the group admin can invite shops.");
        if ((await store.Members(group.Id, ct)).Count(m => m.IsMember) >= BuyingGroup.MaxMembers) return Result<InviteResponse>.Fail("buying_group.full", $"A group can have at most {BuyingGroup.MaxMembers} shops.");

        var (invite, code) = BuyingGroupInvite.Create(group.Id, c.CallerPersonId, clock.GetUtcNow());
        store.Add(invite);
        await store.Save(ct);
        return Result<InviteResponse>.Ok(new(group.Id, code, invite.ExpiresAt));
    }

    /// <summary>A shop joins with a code. The code works once; a wrong or used code says nothing about which groups exist.</summary>
    public async Task<Result<BuyingGroupResponse>> Handle(AcceptGroupInvite c, CancellationToken ct)
    {
        if (!await access.Has(c.Request.CallerPersonId, c.ShopId, "shop.settings", ct)) return Result<BuyingGroupResponse>.Fail("permission.denied", "Only the shop owner can join a group.");
        var invite = string.IsNullOrWhiteSpace(c.Request.Code) ? null : await store.InviteByHash(BuyingGroupInvite.Hash(c.Request.Code), ct);
        if (invite is null) return Result<BuyingGroupResponse>.Fail("invite.invalid", "That code is not right.");
        var group = await store.Get(invite.GroupId, ct);
        if (group is null || !group.IsActive) return Result<BuyingGroupResponse>.Fail("invite.invalid", "That code is not right.");

        var now = clock.GetUtcNow();
        var existing = await store.Member(group.Id, c.ShopId, ct);
        if (existing is { IsMember: true }) return Result<BuyingGroupResponse>.Fail("buying_group.already_member", "Your shop is already in this group.");
        if ((await store.Members(group.Id, ct)).Count(m => m.IsMember) >= BuyingGroup.MaxMembers) return Result<BuyingGroupResponse>.Fail("buying_group.full", $"A group can have at most {BuyingGroup.MaxMembers} shops.");
        var used = invite.Use(c.ShopId, now);
        if (!used.IsSuccess) return Result<BuyingGroupResponse>.Fail(used.Error!.Code, used.Error.Message);

        if (existing is null) store.Add(BuyingGroupMember.Join(group.Id, c.ShopId, c.Request.CallerPersonId, now));
        else existing.Rejoin(now);
        await store.Save(ct);
        return Result<BuyingGroupResponse>.Ok(await Respond(group, c.ShopId, ct));
    }

    public async Task<Result<Unit>> Handle(LeaveBuyingGroup c, CancellationToken ct)
    {
        if (!await access.Has(c.CallerPersonId, c.ShopId, "shop.settings", ct)) return Result<Unit>.Fail("permission.denied", "Only the shop owner can leave a group.");
        var group = await store.Get(c.GroupId, ct);
        var member = group is null ? null : await store.Member(group.Id, c.ShopId, ct);
        if (group is null || member is not { IsMember: true }) return Result<Unit>.Fail("buying_group.not_found", "Group not found.");
        if (group.AdminShopId == c.ShopId) return Result<Unit>.Fail("buying_group.admin_cannot_leave", "The admin shop cannot leave its own group.");
        member.Leave(removed: false);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Unit>> Handle(RemoveGroupMember c, CancellationToken ct)
    {
        var group = await store.Get(c.GroupId, ct);
        if (group is null) return Result<Unit>.Fail("buying_group.not_found", "Group not found.");
        if (!await IsAdmin(group, c.CallerPersonId, ct)) return Result<Unit>.Fail("permission.denied", "Only the group admin can remove a shop.");
        if (c.ShopId == group.AdminShopId) return Result<Unit>.Fail("buying_group.admin_cannot_leave", "The admin shop cannot be removed.");
        var member = await store.Member(group.Id, c.ShopId, ct);
        if (member is not { IsMember: true }) return Result<Unit>.Fail("buying_group.not_found", "That shop is not in the group.");
        member.Leave(removed: true);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<List<BuyingGroupResponse>>> Handle(ListBuyingGroups q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.ShopId, "shop.settings", ct)) return Result<List<BuyingGroupResponse>>.Fail("permission.denied", "Only the shop owner can see this.");
        var list = new List<BuyingGroupResponse>();
        foreach (var g in (await store.ForShop(q.ShopId, ct)).Where(g => g.IsActive).OrderBy(g => g.Name)) list.Add(await Respond(g, q.ShopId, ct));
        return Result<List<BuyingGroupResponse>>.Ok(list);
    }

    /// <summary>Members see the group's deals with their own quantity; only the admin also sees each member's open quantity. Other shops are never named to members.</summary>
    public async Task<Result<GroupHistoryResponse>> Handle(GetGroupHistory q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.ShopId, "stock.receive", ct)) return Result<GroupHistoryResponse>.Fail("permission.denied", "You do not have access to this shop.");
        var group = await store.Get(q.GroupId, ct);
        var member = group is null ? null : await store.Member(group.Id, q.ShopId, ct);
        if (group is null || member is not { IsMember: true }) return Result<GroupHistoryResponse>.Fail("buying_group.not_found", "Group not found.");

        var groupDeals = (await deals.ForBuyingGroups([group.Id], ct)).OrderByDescending(d => d.ClosesAt).ToList();
        var responses = new List<GroupDealResponse>();
        foreach (var d in groupDeals)
        {
            var supplier = await suppliers.Get(d.SupplierId, ct);
            responses.Add(GroupMapping.ToResponse(d, supplier?.Name ?? "", await products.Get(d.SupplierProductId, ct), q.ShopId));
        }

        List<MemberResponse>? members = null;
        if (group.AdminShopId == q.ShopId && await IsAdmin(group, q.CallerPersonId, ct))
        {
            members = [];
            foreach (var m in (await store.Members(group.Id, ct)).Where(m => m.IsMember).OrderBy(m => m.JoinedAt))
            {
                var shop = await shops.Get(m.ShopId, ct);
                members.Add(new(m.ShopId, shop?.Name ?? "", m.JoinedAt, groupDeals.Where(d => d.Status == GroupDealStatus.Open).Sum(d => d.CasesOf(m.ShopId))));
            }
        }
        return Result<GroupHistoryResponse>.Ok(new(group.Id, group.Name, responses, members));
    }
}
