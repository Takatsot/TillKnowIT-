using Billing.Contracts;
using Identity.Application.Shops;
using Identity.Contracts;
using Identity.Domain;

namespace Identity.Application.Tests;

public class BranchTests
{
    private sealed class Store : IBranchStore
    {
        public List<Organisation> Shops { get; } = [];
        public List<Membership> Memberships { get; } = [];
        public Task<Organisation?> GetShop(Guid shopId, CancellationToken ct) => Task.FromResult(Shops.FirstOrDefault(s => s.Id == shopId));
        public Task<Membership?> MembershipAt(Guid personId, Guid organisationId, CancellationToken ct) => Task.FromResult(Memberships.FirstOrDefault(m => m.PersonId == personId && m.OrganisationId == organisationId));
        public Task<Guid> RoleId(string code, CancellationToken ct) => Task.FromResult(Guid.Parse("00000000-0000-0000-0000-00000000000a"));
        public Task Add(Organisation shop, Membership owner, CancellationToken ct) { Shops.Add(shop); Memberships.Add(owner); return Task.CompletedTask; }
        public Task<List<Organisation>> BranchesOf(Guid headId, CancellationToken ct) => Task.FromResult(Shops.Where(s => s.ParentId == headId).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Access : IOrganisationAccess
    {
        public HashSet<(Guid, string)> Granted { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, perm)));
    }

    private sealed class Plans : IEntitlements
    {
        public bool MultiBranch { get; set; } = true;
        public Task<bool> Has(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult(MultiBranch && feature == PlanFeatures.MultiBranch);
        public Task<long?> LimitFor(Guid o, string audience, string feature, CancellationToken ct) => Task.FromResult<long?>(null);
    }

    private sealed class Lookups : Catalogue.Contracts.ILookupChecker
    {
        public Task<bool> IsActive(string type, string code, CancellationToken ct) => Task.FromResult(code == "JHB");
    }

    private readonly Store _store = new();
    private readonly Plans _plans = new();
    private readonly Access _access;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Organisation _head;

    public BranchTests()
    {
        _access = new Access();
        _head = Organisation.CreateShop("Thandi's Spaza", _owner, "JHB");
        _store.Shops.Add(_head);
        _store.Memberships.Add(new Membership(_owner, _head.Id, Guid.NewGuid()));
        _access.Granted.Add((_owner, "shop.settings"));
        _access.Granted.Add((_owner, "report.view"));
        _access.Granted.Add((_owner, "staff.manage"));
    }

    private BranchHandlers Handlers => new(_store, _access, _plans, new Lookups());

    [Fact]
    public async Task The_owner_adds_a_branch_that_starts_pending_and_inherits_the_area_and_the_owner_sees_every_branch_from_then_on()
    {
        var branch = (await Handlers.Handle(new AddBranch(_head.Id, new(_owner, "Soweto branch", null)), default)).Value!;

        Assert.Equal(("Soweto branch", false, "pending", "JHB"), (branch.Name, branch.IsHead, branch.Status, branch.MunicipalityCode));
        Assert.Equal(_head.Id, _store.Shops.Single(s => s.Id == branch.ShopId).ParentId);
        Assert.Contains(_store.Memberships, m => m.PersonId == _owner && m.OrganisationId == branch.ShopId);
        Assert.Equal(MembershipScope.AllBranches, _store.Memberships.First(m => m.OrganisationId == _head.Id).Scope);
    }

    [Fact]
    public async Task Branches_need_the_owner_a_plan_with_branches_a_known_area_and_a_name()
    {
        Assert.Equal("permission.denied", (await Handlers.Handle(new AddBranch(_head.Id, new(Guid.NewGuid(), "B", null)), default)).Error!.Code);
        _plans.MultiBranch = false;
        Assert.Equal("plan.feature_not_included", (await Handlers.Handle(new AddBranch(_head.Id, new(_owner, "B", null)), default)).Error!.Code);
        _plans.MultiBranch = true;
        Assert.Equal("shop.area_invalid", (await Handlers.Handle(new AddBranch(_head.Id, new(_owner, "B", "NOWHERE")), default)).Error!.Code);
        Assert.Equal("shop.name_required", (await Handlers.Handle(new AddBranch(_head.Id, new(_owner, " ", null)), default)).Error!.Code);
        Assert.Equal("shop.not_found", (await Handlers.Handle(new AddBranch(Guid.NewGuid(), new(_owner, "B", null)), default)).Error!.Code);
    }

    [Fact]
    public async Task A_branch_cannot_have_branches_of_its_own()
    {
        var branch = (await Handlers.Handle(new AddBranch(_head.Id, new(_owner, "One", null)), default)).Value!;

        Assert.Equal("branch.nested", (await Handlers.Handle(new AddBranch(branch.ShopId, new(_owner, "Two", null)), default)).Error!.Code);
    }

    [Fact]
    public async Task Listing_from_a_branch_or_the_head_gives_the_whole_group_head_first()
    {
        var one = (await Handlers.Handle(new AddBranch(_head.Id, new(_owner, "Brakpan", null)), default)).Value!;
        await Handlers.Handle(new AddBranch(_head.Id, new(_owner, "Alex", null)), default);

        var fromBranch = (await Handlers.Handle(new ListBranches(one.ShopId, _owner), default)).Value!;

        Assert.Equal(["Thandi's Spaza", "Alex", "Brakpan"], fromBranch.Select(b => b.Name));
        Assert.True(fromBranch[0].IsHead);
        Assert.Equal("permission.denied", (await Handlers.Handle(new ListBranches(_head.Id, Guid.NewGuid()), default)).Error!.Code);
    }

    [Fact]
    public async Task A_general_manager_at_the_head_shop_can_be_given_every_branch_but_only_by_someone_who_manages_staff()
    {
        var manager = Guid.NewGuid();
        _store.Memberships.Add(new Membership(manager, _head.Id, Guid.NewGuid()));

        Assert.Equal("permission.denied", (await Handlers.Handle(new GrantAllBranches(_head.Id, new(manager, manager)), default)).Error!.Code);
        Assert.Equal("staff.unknown", (await Handlers.Handle(new GrantAllBranches(_head.Id, new(_owner, Guid.NewGuid())), default)).Error!.Code);
        Assert.True((await Handlers.Handle(new GrantAllBranches(_head.Id, new(_owner, manager)), default)).IsSuccess);
        Assert.Equal(MembershipScope.AllBranches, _store.Memberships.Single(m => m.PersonId == manager).Scope);
    }
}
