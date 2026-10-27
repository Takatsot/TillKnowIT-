using Identity.Application.Setup;
using Identity.Application.Shops;
using Identity.Application.Staff;
using Identity.Domain;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Tests;

public class SupplierOrganisationTests
{
    /// <summary>One in-memory store that serves both interfaces, like the real DbContext-backed one.</summary>
    private sealed class FakeStore : IShopStore, IStaffStore
    {
        public List<Organisation> Organisations { get; } = [];
        public List<Person> People { get; } = [];
        public List<Membership> Memberships { get; } = [];
        public Dictionary<string, Role> Roles { get; } = new()
        {
            ["supplier_admin"] = new Role("supplier_admin", new() { ["en"] = "Supplier admin" }, 6),
            ["supplier_rep"] = new Role("supplier_rep", new() { ["en"] = "Supplier rep" }, 7),
            ["cashier"] = new Role("cashier", new() { ["en"] = "Cashier" }, 3),
        };

        public Task Add(Organisation shop, CancellationToken ct) { Organisations.Add(shop); return Task.CompletedTask; }
        public Task<bool> PersonExists(Guid personId, CancellationToken ct) => Task.FromResult(People.Any(p => p.Id == personId));
        public Task<Organisation?> GetShop(Guid id, CancellationToken ct) => Task.FromResult(Organisations.FirstOrDefault(o => o.Id == id));
        public Task<Role?> FindRole(string code, CancellationToken ct) => Task.FromResult(Roles.GetValueOrDefault(code));
        public Task<Person?> FindPersonByPhone(string phone, CancellationToken ct) => Task.FromResult(People.FirstOrDefault(p => p.Phone == phone));
        public Task<Membership?> GetMembership(Guid id, CancellationToken ct) => Task.FromResult(Memberships.FirstOrDefault(m => m.Id == id));
        public Task<bool> MembershipExists(Guid personId, Guid orgId, CancellationToken ct) => Task.FromResult(Memberships.Any(m => m.PersonId == personId && m.OrganisationId == orgId));
        public Task Add(Person person, CancellationToken ct) { People.Add(person); return Task.CompletedTask; }
        public Task Add(Membership m, CancellationToken ct) { Memberships.Add(m); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private readonly FakeStore _store = new();
    private SupplierOrganisationService Service() => new(_store, _store);

    private Person Person(string phone = "+27821110000")
    {
        var p = new Person("Sipho", phone);
        _store.People.Add(p);
        return p;
    }

    [Fact]
    public async Task A_supplier_organisation_starts_pending_and_its_creator_becomes_the_supplier_admin()
    {
        var applicant = Person();

        var result = await Service().Create("  Fresh Foods (Pty) Ltd ", applicant.Id, default);

        Assert.True(result.IsSuccess);
        var org = Assert.Single(_store.Organisations);
        Assert.Equal(OrganisationType.Supplier, org.Type);
        Assert.Equal(OrganisationStatus.Pending, org.Status);
        Assert.Equal("Fresh Foods (Pty) Ltd", org.Name);
        var membership = Assert.Single(_store.Memberships);
        Assert.Equal((applicant.Id, org.Id, _store.Roles["supplier_admin"].Id), (membership.PersonId, membership.OrganisationId, membership.RoleId));
    }

    [Fact]
    public async Task A_supplier_needs_a_name_and_a_real_person()
    {
        var applicant = Person();
        Assert.Equal("supplier.name_invalid", (await Service().Create(" ", applicant.Id, default)).Error!.Code);
        Assert.Equal("supplier.name_invalid", (await Service().Create(new string('x', 201), applicant.Id, default)).Error!.Code);
        Assert.Equal("person.not_found", (await Service().Create("Fresh Foods", Guid.NewGuid(), default)).Error!.Code);
        Assert.Empty(_store.Organisations);
    }

    [Fact]
    public async Task The_admin_invites_a_rep_who_gets_the_supplier_rep_role_and_a_cleaned_phone_number()
    {
        var orgId = (await Service().Create("Fresh Foods", Person().Id, default)).Value;

        var result = await Service().AddRep(orgId, " Thandi ", "082 555 0000", default);

        Assert.True(result.IsSuccess);
        var rep = _store.People.Single(p => p.Phone == "+27825550000");
        Assert.Equal("Thandi", rep.DisplayName);
        var membership = _store.Memberships.Single(m => m.PersonId == rep.Id);
        Assert.Equal(_store.Roles["supplier_rep"].Id, membership.RoleId);
    }

    [Fact]
    public async Task An_existing_person_is_reused_and_cannot_be_added_to_the_same_supplier_twice()
    {
        var orgId = (await Service().Create("Fresh Foods", Person().Id, default)).Value;
        var existing = Person("+27825550000");

        Assert.True((await Service().AddRep(orgId, "Thandi", "0825550000", default)).IsSuccess);
        Assert.Single(_store.People, p => p.Id == existing.Id);

        var again = await Service().AddRep(orgId, "Thandi", "0825550000", default);
        Assert.Equal("staff.already_member", again.Error!.Code);
    }

    [Fact]
    public async Task Reps_can_only_be_added_to_a_supplier_with_a_good_name_and_phone()
    {
        var shop = Organisation.CreateShop("A shop", Guid.NewGuid());
        _store.Organisations.Add(shop);
        var orgId = (await Service().Create("Fresh Foods", Person().Id, default)).Value;

        Assert.Equal("supplier.not_found", (await Service().AddRep(shop.Id, "Thandi", "0825550000", default)).Error!.Code);
        Assert.Equal("supplier.not_found", (await Service().AddRep(Guid.NewGuid(), "Thandi", "0825550000", default)).Error!.Code);
        Assert.Equal("staff.name_required", (await Service().AddRep(orgId, " ", "0825550000", default)).Error!.Code);
        Assert.Equal("staff.phone_invalid", (await Service().AddRep(orgId, "Thandi", "123", default)).Error!.Code);
    }

    [Fact]
    public async Task Status_moves_between_pending_verified_and_suspended_and_only_a_supplier_can_be_changed()
    {
        var orgId = (await Service().Create("Fresh Foods", Person().Id, default)).Value;
        var org = _store.Organisations.Single(o => o.Id == orgId);

        Assert.True((await Service().SetStatus(orgId, "verified", default)).IsSuccess);
        Assert.Equal(OrganisationStatus.Verified, org.Status);
        Assert.True((await Service().SetStatus(orgId, "suspended", default)).IsSuccess);
        Assert.Equal(OrganisationStatus.Suspended, org.Status);
        Assert.Equal("shop.suspended", (await Service().SetStatus(orgId, "verified", default)).Error!.Code);
        Assert.True((await Service().SetStatus(orgId, "pending", default)).IsSuccess);
        Assert.Equal(OrganisationStatus.Pending, org.Status);

        Assert.Equal("supplier.status_invalid", (await Service().SetStatus(orgId, "banned", default)).Error!.Code);
        var shop = Organisation.CreateShop("A shop", Guid.NewGuid());
        _store.Organisations.Add(shop);
        Assert.Equal("supplier.not_found", (await Service().SetStatus(shop.Id, "verified", default)).Error!.Code);
    }
}
