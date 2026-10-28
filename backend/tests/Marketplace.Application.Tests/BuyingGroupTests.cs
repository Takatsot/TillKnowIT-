using Identity.Contracts;
using Marketplace.Application.Orders;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using Microsoft.Extensions.Time.Testing;
using TillKnowIT.Dispatching;

namespace Marketplace.Application.Tests;

public class BuyingGroupDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_group_needs_a_name_and_a_known_kind()
    {
        Assert.Equal("buying_group.name_invalid", BuyingGroup.Create(" ", "stokvel", Guid.NewGuid(), Guid.NewGuid(), Now).Error!.Code);
        Assert.Equal("buying_group.name_invalid", BuyingGroup.Create(new string('x', 101), "stokvel", Guid.NewGuid(), Guid.NewGuid(), Now).Error!.Code);
        Assert.Equal("buying_group.kind_invalid", BuyingGroup.Create("Ladies", "club", Guid.NewGuid(), Guid.NewGuid(), Now).Error!.Code);
        Assert.Equal(BuyingGroupKind.School, BuyingGroup.Create("Tuckshops of Soweto", "School", Guid.NewGuid(), Guid.NewGuid(), Now).Value!.Kind);
    }

    [Fact]
    public void An_invite_code_is_eight_easy_letters_works_once_in_any_case_and_expires_after_seven_days()
    {
        var (invite, code) = BuyingGroupInvite.Create(Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.Equal(8, code.Length);
        Assert.DoesNotContain(code, c => "01OI".Contains(c));
        Assert.NotEqual(code, invite.CodeHash);
        Assert.Equal(BuyingGroupInvite.Hash(code), BuyingGroupInvite.Hash(code.ToLowerInvariant().Insert(4, "-")));
        Assert.True(invite.Use(Guid.NewGuid(), Now.AddDays(6)).IsSuccess);
        Assert.Equal("invite.used", invite.Use(Guid.NewGuid(), Now.AddDays(6)).Error!.Code);

        var (late, _) = BuyingGroupInvite.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.Equal("invite.expired", late.Use(Guid.NewGuid(), Now.AddDays(7)).Error!.Code);
    }
}

public class BuyingGroupFlowTests
{
    private sealed class Store : IBuyingGroupStore
    {
        public List<BuyingGroup> Groups { get; } = [];
        public List<BuyingGroupMember> MemberRows { get; } = [];
        public List<BuyingGroupInvite> Invites { get; } = [];
        public void Add(BuyingGroup g) => Groups.Add(g);
        public Task<BuyingGroup?> Get(Guid id, CancellationToken ct) => Task.FromResult(Groups.FirstOrDefault(g => g.Id == id));
        public Task<List<BuyingGroup>> ForShop(Guid shopId, CancellationToken ct) => Task.FromResult(Groups.Where(g => MemberRows.Any(m => m.GroupId == g.Id && m.ShopId == shopId && m.IsMember)).ToList());
        public void Add(BuyingGroupMember m) => MemberRows.Add(m);
        public Task<BuyingGroupMember?> Member(Guid groupId, Guid shopId, CancellationToken ct) => Task.FromResult(MemberRows.FirstOrDefault(m => m.GroupId == groupId && m.ShopId == shopId));
        public Task<List<BuyingGroupMember>> Members(Guid groupId, CancellationToken ct) => Task.FromResult(MemberRows.Where(m => m.GroupId == groupId).ToList());
        public Task<List<Guid>> GroupIdsOf(Guid shopId, CancellationToken ct) => Task.FromResult(MemberRows.Where(m => m.ShopId == shopId && m.IsMember).Select(m => m.GroupId).ToList());
        public void Add(BuyingGroupInvite i) => Invites.Add(i);
        public Task<BuyingGroupInvite?> InviteByHash(string hash, CancellationToken ct) => Task.FromResult(Invites.FirstOrDefault(i => i.CodeHash == hash));
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Deals : IGroupDealStore
    {
        public List<GroupDeal> All { get; } = [];
        public Task Add(GroupDeal d, CancellationToken ct) { All.Add(d); return Task.CompletedTask; }
        public Task<GroupDeal?> Get(Guid id, CancellationToken ct) => Task.FromResult(All.FirstOrDefault(d => d.Id == id));
        public Task<List<GroupDeal>> OpenInArea(string area, CancellationToken ct) => Task.FromResult(All.Where(d => d.AreaCode == area && d.Status == GroupDealStatus.Open).ToList());
        public Task<List<GroupDeal>> ForSupplier(Guid id, CancellationToken ct) => Task.FromResult(All.Where(d => d.SupplierId == id).ToList());
        public Task<List<GroupDeal>> ForBuyingGroups(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult(All.Where(d => d.BuyingGroupId != null && ids.Contains(d.BuyingGroupId.Value)).ToList());
        public Task<List<GroupDeal>> NeedingClosure(DateTimeOffset now, int take, CancellationToken ct) => Task.FromResult(new List<GroupDeal>());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Suppliers : ISupplierStore, ISupplierProductStore
    {
        public Supplier? One { get; set; }
        public SupplierProduct? Product { get; set; }
        public Task Add(Supplier s, CancellationToken ct) => Task.CompletedTask;
        Task<Supplier?> ISupplierStore.Get(Guid id, CancellationToken ct) => Task.FromResult(One);
        public Task<List<Supplier>> Pending(CancellationToken ct) => Task.FromResult(new List<Supplier>());
        public List<SupplierArea> AreaList { get; } = [];
        public Task<List<SupplierArea>> Areas(Guid id, CancellationToken ct) => Task.FromResult(AreaList.ToList());
        public Task ReplaceAreas(Guid id, List<SupplierArea> a, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<string, SupplierProduct>> ByBarcode(Guid id, CancellationToken ct) => Task.FromResult(new Dictionary<string, SupplierProduct>());
        Task<SupplierProduct?> ISupplierProductStore.Get(Guid id, CancellationToken ct) => Task.FromResult(Product);
        public Task<List<SupplierProduct>> List(Guid id, CancellationToken ct) => Task.FromResult(new List<SupplierProduct>());
        public Task Add(SupplierProduct p, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Shops : IShopDirectory
    {
        public Dictionary<Guid, string> Areas { get; } = [];
        public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) => Task.FromResult<ShopInfo?>(new(shopId, "Shop " + shopId.ToString("N")[..4], "verified", null, null, null, "en", Areas.GetValueOrDefault(shopId)));
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(new List<Guid>());
    }

    private sealed class Access : IOrganisationAccess
    {
        public HashSet<(Guid Person, Guid Shop)> Owners { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Owners.Contains((p, o)));
    }

    private static readonly DateTimeOffset Start = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private readonly Store _store = new();
    private readonly Deals _deals = new();
    private readonly Suppliers _suppliers = new();
    private readonly Shops _shops = new();
    private readonly Access _access = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly Guid _adminShop = Guid.NewGuid();
    private readonly Guid _adminPerson = Guid.NewGuid();
    private readonly Guid _shopB = Guid.NewGuid();
    private readonly Guid _personB = Guid.NewGuid();
    private readonly Guid _shopC = Guid.NewGuid();
    private readonly Guid _personC = Guid.NewGuid();

    public BuyingGroupFlowTests()
    {
        _access.Owners.Add((_adminPerson, _adminShop));
        _access.Owners.Add((_personB, _shopB));
        _access.Owners.Add((_personC, _shopC));
        _shops.Areas[_adminShop] = "jhb";
        _shops.Areas[_shopB] = "cpt";   // a different area on purpose: a stokvel can be spread out
        _shops.Areas[_shopC] = "jhb";

        var org = Guid.NewGuid();
        var supplier = Supplier.Apply(new SupplierApplication(Guid.NewGuid(), org, "Fresh Foods", null, "Sipho", "0821234567", "0825550000", "orders@fresh.co.za", 1), Start).Value!;
        supplier.Verify(Guid.NewGuid(), SupplierVerification.Items, Start);
        _suppliers.One = supplier;
        _suppliers.Product = SupplierProduct.Create(supplier.Id, null, new SupplierProductInput("6001111111111", "Coke 330ml", 24, 24000, true, true, null), Start).Value!;
    }

    private BuyingGroupHandlers Handlers => new(_store, _deals, _suppliers, _suppliers, _shops, _access, _clock);
    private JoinGroupDealHandler Joiner => new(_deals, _suppliers, _suppliers, _shops, _access, _clock, _store);
    private GroupDealQueries Queries => new(_deals, _suppliers, _suppliers, _shops, _access, _store);

    private async Task<BuyingGroupResponse> Create() => (await Handlers.Handle(new CreateBuyingGroup(new(_adminPerson, _adminShop, "Soweto Stokvel", "stokvel")), default)).Value!;

    private async Task Join(Guid group, Guid shop, Guid person)
    {
        var code = (await Handlers.Handle(new InviteToGroup(group, _adminPerson), default)).Value!.Code;
        Assert.True((await Handlers.Handle(new AcceptGroupInvite(shop, new(person, code)), default)).IsSuccess);
    }

    private GroupDeal DealFor(Guid? group, string area = "jhb")
    {
        var deal = GroupDeal.Create(_suppliers.One!.Id, new(_suppliers.Product!.Id, 22_000, 10, Start.AddDays(7), area, group), [area], Start).Value!;
        _deals.All.Add(deal);
        return deal;
    }

    [Fact]
    public async Task The_owner_starts_a_group_and_the_shop_is_its_admin_and_first_member()
    {
        var group = await Create();

        Assert.Equal(("Soweto Stokvel", "stokvel", 1, true), (group.Name, group.Kind, group.Members, group.YouAreAdmin));
        Assert.Equal("permission.denied", (await Handlers.Handle(new CreateBuyingGroup(new(Guid.NewGuid(), _adminShop, "X", "stokvel")), default)).Error!.Code);
    }

    [Fact]
    public async Task A_shop_joins_with_the_admins_code_once_and_a_second_try_with_the_same_code_fails()
    {
        var group = await Create();
        var code = (await Handlers.Handle(new InviteToGroup(group.Id, _adminPerson), default)).Value!.Code;

        var joined = (await Handlers.Handle(new AcceptGroupInvite(_shopB, new(_personB, code)), default)).Value!;

        Assert.Equal((2, false), (joined.Members, joined.YouAreAdmin));
        Assert.Equal("invite.used", (await Handlers.Handle(new AcceptGroupInvite(_shopC, new(_personC, code)), default)).Error!.Code);
        Assert.Equal(2, _store.MemberRows.Count);
    }

    [Fact]
    public async Task A_wrong_code_a_stranger_and_a_member_asking_again_are_all_refused_without_saying_which_groups_exist()
    {
        var group = await Create();
        var code = (await Handlers.Handle(new InviteToGroup(group.Id, _adminPerson), default)).Value!.Code;

        Assert.Equal("invite.invalid", (await Handlers.Handle(new AcceptGroupInvite(_shopB, new(_personB, "ZZZZZZZZ")), default)).Error!.Code);
        Assert.Equal("invite.invalid", (await Handlers.Handle(new AcceptGroupInvite(_shopB, new(_personB, " ")), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new AcceptGroupInvite(_shopB, new(Guid.NewGuid(), code)), default)).Error!.Code);
        Assert.Equal("buying_group.already_member", (await Handlers.Handle(new AcceptGroupInvite(_adminShop, new(_adminPerson, code)), default)).Error!.Code);
        _clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal("invite.expired", (await Handlers.Handle(new AcceptGroupInvite(_shopB, new(_personB, code)), default)).Error!.Code);
    }

    [Fact]
    public async Task Only_the_group_admin_invites_and_removes_and_the_admin_shop_cannot_leave_or_be_removed()
    {
        var group = await Create();
        await Join(group.Id, _shopB, _personB);

        Assert.Equal("permission.denied", (await Handlers.Handle(new InviteToGroup(group.Id, _personB), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new RemoveGroupMember(group.Id, _shopB, _personB), default)).Error!.Code);
        Assert.Equal("buying_group.admin_cannot_leave", (await Handlers.Handle(new RemoveGroupMember(group.Id, _adminShop, _adminPerson), default)).Error!.Code);
        Assert.Equal("buying_group.admin_cannot_leave", (await Handlers.Handle(new LeaveBuyingGroup(group.Id, _adminShop, _adminPerson), default)).Error!.Code);
        Assert.Equal("buying_group.not_found", (await Handlers.Handle(new InviteToGroup(Guid.NewGuid(), _adminPerson), default)).Error!.Code);
    }

    [Fact]
    public async Task A_member_can_leave_and_come_back_with_a_new_code_and_a_removed_shop_loses_its_view_of_the_groups_deals()
    {
        var group = await Create();
        await Join(group.Id, _shopB, _personB);
        var deal = DealFor(group.Id);

        Assert.True((await Handlers.Handle(new LeaveBuyingGroup(group.Id, _shopB, _personB), default)).IsSuccess);
        Assert.Equal("group.not_found", (await Joiner.Handle(new JoinGroupDeal(deal.Id, _shopB, new(_personB, 1)), default)).Error!.Code);

        await Join(group.Id, _shopB, _personB);
        Assert.True((await Joiner.Handle(new JoinGroupDeal(deal.Id, _shopB, new(_personB, 1)), default)).IsSuccess);

        await Handlers.Handle(new RemoveGroupMember(group.Id, _shopB, _adminPerson), default);
        Assert.Equal("group.not_found", (await Joiner.Handle(new JoinGroupDeal(deal.Id, _shopB, new(_personB, 2)), default)).Error!.Code);
        Assert.Equal(1, (await Handlers.Handle(new ListBuyingGroups(_adminShop, _adminPerson), default)).Value!.Single().Members);
    }

    // ---- invite-only deals ----

    [Fact]
    public async Task An_invite_only_deal_is_seen_and_joined_by_members_anywhere_and_by_nobody_else_even_in_the_same_area()
    {
        var group = await Create();
        await Join(group.Id, _shopB, _personB);   // Cape Town: the deal is for Johannesburg
        var deal = DealFor(group.Id, "jhb");

        var member = (await Queries.Handle(new ListGroupDeals(_shopB, _personB), default)).Value!;
        var outsider = (await Queries.Handle(new ListGroupDeals(_shopC, _personC), default)).Value!;   // in Johannesburg, but not in the group

        Assert.Equal([deal.Id], member.Select(d => d.Id));
        Assert.Equal(group.Id, member.Single().BuyingGroupId);
        Assert.Empty(outsider);
        Assert.Equal("group.not_found", (await Joiner.Handle(new JoinGroupDeal(deal.Id, _shopC, new(_personC, 2)), default)).Error!.Code);
        Assert.Equal(3, (await Joiner.Handle(new JoinGroupDeal(deal.Id, _shopB, new(_personB, 3)), default)).Value!.TotalCases);
    }

    [Fact]
    public async Task A_public_deal_still_follows_the_area_and_does_not_need_a_group()
    {
        var open = DealFor(null, "jhb");

        Assert.Equal([open.Id], (await Queries.Handle(new ListGroupDeals(_shopC, _personC), default)).Value!.Select(d => d.Id));
        Assert.Empty((await Queries.Handle(new ListGroupDeals(_shopB, _personB), default)).Value!);
        Assert.Equal("group.not_in_area", (await Joiner.Handle(new JoinGroupDeal(open.Id, _shopB, new(_personB, 1)), default)).Error!.Code);
    }

    // ---- shared history ----

    [Fact]
    public async Task Members_see_the_groups_deals_with_their_own_quantity_and_the_admin_alone_sees_each_members_open_quantity()
    {
        var group = await Create();
        await Join(group.Id, _shopB, _personB);
        var deal = DealFor(group.Id);
        deal.Commit(_adminShop, _adminPerson, 4, Start);
        deal.Commit(_shopB, _personB, 2, Start);
        DealFor(null);   // a public deal is not the group's

        var asMember = (await Handlers.Handle(new GetGroupHistory(group.Id, _shopB, _personB), default)).Value!;
        var asAdmin = (await Handlers.Handle(new GetGroupHistory(group.Id, _adminShop, _adminPerson), default)).Value!;

        Assert.Equal((1, 2, 6), (asMember.Deals.Count, asMember.Deals.Single().YourCases, asMember.Deals.Single().TotalCases));
        Assert.Null(asMember.Members);
        Assert.Equal([4, 2], asAdmin.Members!.Select(m => m.CasesOnOpenDeals));
        Assert.Equal("buying_group.not_found", (await Handlers.Handle(new GetGroupHistory(group.Id, _shopC, _personC), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new GetGroupHistory(group.Id, _shopB, Guid.NewGuid()), default)).Error!.Code);
    }

    // ---- posting an invite-only deal ----

    [Fact]
    public async Task A_supplier_can_make_a_deal_for_a_group_that_exists_and_not_for_one_that_does_not()
    {
        var person = Guid.NewGuid();
        var org = Guid.NewGuid();
        var supplier = Supplier.Apply(new SupplierApplication(person, org, "Fresh Foods", null, "Sipho", "0821234567", "0825550000", "orders@fresh.co.za", 1), Start).Value!;
        supplier.Verify(Guid.NewGuid(), SupplierVerification.Items, Start);
        _suppliers.One = supplier;
        _suppliers.Product = SupplierProduct.Create(supplier.Id, null, new SupplierProductInput("6001111111111", "Coke 330ml", 24, 24000, true, true, null), Start).Value!;
        _suppliers.AreaList.Add(new SupplierArea(supplier.Id, "jhb", DeliveryDays.Mon));
        _access.Owners.Add((person, org));
        var group = await Create();
        var handler = new PostGroupDealHandler(_suppliers, _suppliers, _deals, _access, _clock, _store);

        var missing = await handler.Handle(new PostGroupDeal(supplier.Id, new(person, _suppliers.Product.Id, 22_000, 10, Start.AddDays(5), "jhb", Guid.NewGuid())), default);
        var made = await handler.Handle(new PostGroupDeal(supplier.Id, new(person, _suppliers.Product.Id, 22_000, 10, Start.AddDays(5), "jhb", group.Id)), default);

        Assert.Equal("buying_group.not_found", missing.Error!.Code);
        Assert.Equal(group.Id, made.Value!.BuyingGroupId);
    }
}
