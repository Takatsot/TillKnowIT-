using Identity.Application.Setup;
using Identity.Contracts;
using Identity.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Identity.Application.Tests;

public class SetupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore : ISetupStore
    {
        public Organisation? Shop { get; set; } = Organisation.CreateShop("Mama's Spaza", Guid.NewGuid());
        public List<SetupStep> Done { get; } = [];
        public int Saves { get; private set; }
        public Task<Organisation?> GetShop(Guid shopId, CancellationToken ct) => Task.FromResult(Shop);
        public Task<List<SetupStep>> Steps(Guid shopId, CancellationToken ct) => Task.FromResult(Done.ToList());
        public Task Add(SetupStep step, CancellationToken ct) { Done.Add(step); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) { Saves++; return Task.CompletedTask; }
    }

    private sealed class FakePermissions : IPermissionChecker
    {
        public HashSet<(Guid Person, string Permission)> Granted { get; } = [];
        public Task<bool> Has(Guid personId, string permission, CancellationToken ct) => Task.FromResult(Granted.Contains((personId, permission)));
    }

    private readonly FakeStore _store = new();
    private readonly FakePermissions _permissions = new();
    private readonly Guid _agent = Guid.NewGuid();
    private readonly Guid _shop = Guid.NewGuid();

    public SetupTests()
    {
        _permissions.Granted.Add((_agent, "shop.setup"));
        _permissions.Granted.Add((_agent, "shop.verify"));
    }

    private CompleteSetupStepHandler Complete() => new(_store, _permissions, new FakeTimeProvider(Now));
    private VerifyShopHandler Verify() => new(_store, _permissions);

    private async Task DoAll(params string[] except)
    {
        foreach (var item in SetupChecklist.Items.Except(except))
            Assert.True((await Complete().Handle(new CompleteSetupStep(_shop, _agent, item, null), default)).IsSuccess);
    }

    [Fact]
    public void The_checklist_has_the_seven_steps_from_the_spec()
    {
        Assert.Equal(["device_check", "shop_details", "document_photos", "catalogue_import", "staff_pins", "test_sale", "training_signoff"], SetupChecklist.Items);
        Assert.False(SetupChecklist.IsComplete(SetupChecklist.Items.Take(6)));
        Assert.True(SetupChecklist.IsComplete(SetupChecklist.Items));
        Assert.False(SetupChecklist.IsItem("coffee"));
    }

    [Fact]
    public async Task A_field_agent_ticks_off_a_step_once_and_doing_it_again_changes_nothing()
    {
        var first = await Complete().Handle(new CompleteSetupStep(_shop, _agent, "device_check", "Tecno phone works offline"), default);
        var again = await Complete().Handle(new CompleteSetupStep(_shop, _agent, "device_check", "second try"), default);

        Assert.True(first.IsSuccess && again.IsSuccess);
        var step = Assert.Single(_store.Done);
        Assert.Equal("Tecno phone works offline", step.Note);
        Assert.Equal(_agent, step.CompletedBy);
        Assert.Equal(Now, step.CompletedAt);
    }

    [Fact]
    public async Task Only_someone_with_the_setup_permission_can_tick_steps_and_the_step_must_exist()
    {
        var owner = Guid.NewGuid();
        Assert.Equal("permission.denied", (await Complete().Handle(new CompleteSetupStep(_shop, owner, "device_check", null), default)).Error!.Code);
        Assert.Equal("setup.item_invalid", (await Complete().Handle(new CompleteSetupStep(_shop, _agent, "coffee", null), default)).Error!.Code);

        _store.Shop = null;
        Assert.Equal("shop.not_found", (await Complete().Handle(new CompleteSetupStep(_shop, _agent, "device_check", null), default)).Error!.Code);
        Assert.Empty(_store.Done);
    }

    [Fact]
    public async Task The_checklist_shows_what_is_done_and_what_is_left()
    {
        await Complete().Handle(new CompleteSetupStep(_shop, _agent, "staff_pins", null), default);

        var list = (await new GetSetupChecklistHandler(_store).Handle(new GetSetupChecklist(_shop), default)).Value!;

        Assert.Equal(7, list.Steps.Count);
        Assert.Equal(["staff_pins"], list.Steps.Where(s => s.Done).Select(s => s.Item));
        Assert.False(list.AllDone);
        Assert.Equal("pending", list.ShopStatus);
    }

    [Fact]
    public async Task A_shop_cannot_be_verified_until_every_step_is_done_and_then_it_is()
    {
        await DoAll(except: "training_signoff");
        var early = await Verify().Handle(new VerifyShop(_shop, _agent), default);
        Assert.Equal("setup.incomplete", early.Error!.Code);
        Assert.Contains("training_signoff", early.Error.Message);
        Assert.Equal(OrganisationStatus.Pending, _store.Shop!.Status);

        await Complete().Handle(new CompleteSetupStep(_shop, _agent, "training_signoff", null), default);
        var done = await Verify().Handle(new VerifyShop(_shop, _agent), default);

        Assert.True(done.IsSuccess);
        Assert.Equal(OrganisationStatus.Verified, _store.Shop.Status);
    }

    [Fact]
    public async Task Only_someone_with_the_verify_permission_can_verify_and_a_suspended_shop_cannot_be_verified()
    {
        await DoAll();
        Assert.Equal("permission.denied", (await Verify().Handle(new VerifyShop(_shop, Guid.NewGuid()), default)).Error!.Code);

        _store.Shop!.Suspend();
        Assert.Equal("shop.suspended", (await Verify().Handle(new VerifyShop(_shop, _agent), default)).Error!.Code);

        _store.Shop.Reinstate();
        Assert.True((await Verify().Handle(new VerifyShop(_shop, _agent), default)).IsSuccess);
        Assert.Equal(OrganisationStatus.Verified, _store.Shop.Status);
    }

    // ---- memberships (E1-5) ----

    private sealed class FakeMemberships(List<MembershipResponse> rows) : IMembershipReadStore
    {
        public Task<List<MembershipResponse>> ForPerson(Guid personId, CancellationToken ct) => Task.FromResult(rows);
    }

    [Fact]
    public async Task A_person_with_several_places_sees_them_all_in_name_order()
    {
        var rows = new List<MembershipResponse>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Zola's Tuckshop", "shop", "owner"),
            new(Guid.NewGuid(), Guid.NewGuid(), "Ayanda's Spaza", "shop", "cashier"),
        };

        var list = (await new ListMembershipsHandler(new FakeMemberships(rows)).Handle(new ListMemberships(Guid.NewGuid()), default)).Value!;

        Assert.Equal(["Ayanda's Spaza", "Zola's Tuckshop"], list.Select(m => m.OrganisationName));
        Assert.Equal(["cashier", "owner"], list.Select(m => m.RoleCode));
    }
}
