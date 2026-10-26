using Identity.Application.Staff;
using Identity.Domain;
using Identity.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace Identity.Application.Tests;

public class StaffTests
{
    private sealed class PlainHasher : IPinHasher
    {
        public string Hash(string pin) => "h:" + pin;
        public bool Verify(string pin, string hash) => hash == "h:" + pin;
    }

    private sealed class FakeStaffStore : IStaffStore
    {
        public Organisation? Shop { get; set; } = Organisation.CreateShop("Mama's Spaza", Guid.NewGuid());
        public Dictionary<string, Role> Roles { get; } = new()
        {
            ["cashier"] = new Role("cashier", new() { ["en"] = "Cashier" }, 3),
            ["manager"] = new Role("manager", new() { ["en"] = "Manager" }, 2),
            ["owner"] = new Role("owner", new() { ["en"] = "Owner" }, 1),
        };
        public List<Person> People { get; } = [];
        public List<Membership> Memberships { get; } = [];
        public int Saves { get; private set; }

        public Task<Organisation?> GetShop(Guid shopId, CancellationToken ct) => Task.FromResult(Shop);
        public Task<Role?> FindRole(string code, CancellationToken ct) => Task.FromResult(Roles.GetValueOrDefault(code));
        public Task<Person?> FindPersonByPhone(string phone, CancellationToken ct) => Task.FromResult(People.FirstOrDefault(p => p.Phone == phone));
        public Task<Membership?> GetMembership(Guid id, CancellationToken ct) => Task.FromResult(Memberships.FirstOrDefault(m => m.Id == id));
        public Task<bool> MembershipExists(Guid personId, Guid orgId, CancellationToken ct) =>
            Task.FromResult(Memberships.Any(m => m.PersonId == personId && m.OrganisationId == orgId));
        public Task Add(Person person, CancellationToken ct) { People.Add(person); return Task.CompletedTask; }
        public Task Add(Membership m, CancellationToken ct) { Memberships.Add(m); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) { Saves++; return Task.CompletedTask; }
    }

    private static async Task<(FakeStaffStore Store, Guid MembershipId)> StoreWithCashier(string pin = "1234")
    {
        var store = new FakeStaffStore();
        var result = await new AddStaffHandler(store, new PlainHasher())
            .Handle(new AddStaff(Guid.NewGuid(), "Thandi", "082 123 4567", "cashier", pin), default);
        return (store, result.Value);
    }

    // ---- AddStaff ----

    [Fact]
    public async Task Adds_cashier_with_hashed_pin_and_normalised_phone()
    {
        var (store, id) = await StoreWithCashier("4321");

        var person = Assert.Single(store.People);
        Assert.Equal("+27821234567", person.Phone);
        var m = Assert.Single(store.Memberships);
        Assert.Equal(id, m.Id);
        Assert.Equal("h:4321", m.PinHash);
        Assert.NotEqual("4321", m.PinHash);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    [InlineData("")]
    public async Task Rejects_bad_pins(string pin)
    {
        var store = new FakeStaffStore();
        var result = await new AddStaffHandler(store, new PlainHasher())
            .Handle(new AddStaff(Guid.NewGuid(), "Thandi", "0821234567", "cashier", pin), default);

        Assert.Equal("staff.pin_invalid", result.Error!.Code);
        Assert.Empty(store.Memberships);
    }

    [Fact]
    public async Task Owner_role_cannot_be_handed_out_as_staff()
    {
        var store = new FakeStaffStore();
        var result = await new AddStaffHandler(store, new PlainHasher())
            .Handle(new AddStaff(Guid.NewGuid(), "Thandi", "0821234567", "owner", "1234"), default);

        Assert.Equal("staff.role_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Same_person_cannot_be_added_twice_to_one_shop()
    {
        var (store, _) = await StoreWithCashier();
        var again = await new AddStaffHandler(store, new PlainHasher())
            .Handle(new AddStaff(Guid.NewGuid(), "Thandi", "+27821234567", "manager", "9999"), default);

        Assert.Equal("staff.already_member", again.Error!.Code);
        Assert.Single(store.Memberships);
    }

    [Fact]
    public async Task Unknown_shop_is_rejected()
    {
        var store = new FakeStaffStore { Shop = null };
        var result = await new AddStaffHandler(store, new PlainHasher())
            .Handle(new AddStaff(Guid.NewGuid(), "Thandi", "0821234567", "cashier", "1234"), default);

        Assert.Equal("shop.not_found", result.Error!.Code);
    }

    // ---- UpdateStaff ----

    [Fact]
    public async Task Disabled_staff_cannot_verify_pin()
    {
        var (store, id) = await StoreWithCashier();
        await new UpdateStaffHandler(store, new PlainHasher()).Handle(new UpdateStaff(id, null, true), default);

        var result = await new VerifyPinHandler(store, new PlainHasher(), new FakeTimeProvider())
            .Handle(new VerifyPin(id, "1234"), default);

        Assert.Equal("staff.pin_rejected", result.Error!.Code);
    }

    [Fact]
    public async Task Changing_pin_replaces_the_hash()
    {
        var (store, id) = await StoreWithCashier();
        await new UpdateStaffHandler(store, new PlainHasher()).Handle(new UpdateStaff(id, "5555", null), default);

        Assert.Equal("h:5555", store.Memberships.Single().PinHash);
    }

    // ---- VerifyPin and lockout ----

    [Fact]
    public async Task Five_wrong_pins_lock_for_five_minutes_then_unlock()
    {
        var (store, id) = await StoreWithCashier();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
        var handler = new VerifyPinHandler(store, new PlainHasher(), clock);

        for (var i = 0; i < 4; i++)
            Assert.Equal("staff.pin_rejected", (await handler.Handle(new VerifyPin(id, "0000"), default)).Error!.Code);
        Assert.Equal("staff.locked", (await handler.Handle(new VerifyPin(id, "0000"), default)).Error!.Code);

        // Even the right PIN is refused while locked.
        Assert.Equal("staff.locked", (await handler.Handle(new VerifyPin(id, "1234"), default)).Error!.Code);

        clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        Assert.True((await handler.Handle(new VerifyPin(id, "1234"), default)).IsSuccess);
    }

    [Fact]
    public async Task Correct_pin_resets_the_failure_count()
    {
        var (store, id) = await StoreWithCashier();
        var handler = new VerifyPinHandler(store, new PlainHasher(), new FakeTimeProvider());

        for (var i = 0; i < 4; i++) await handler.Handle(new VerifyPin(id, "0000"), default);
        Assert.True((await handler.Handle(new VerifyPin(id, "1234"), default)).IsSuccess);

        // Counter restarted, so four more misses still do not lock.
        for (var i = 0; i < 4; i++) await handler.Handle(new VerifyPin(id, "0000"), default);
        Assert.True((await handler.Handle(new VerifyPin(id, "1234"), default)).IsSuccess);
    }

    // ---- Real hasher ----

    [Fact]
    public void Pbkdf2_hash_verifies_and_is_salted()
    {
        var hasher = new Pbkdf2PinHasher();
        var a = hasher.Hash("1234");
        var b = hasher.Hash("1234");

        Assert.NotEqual(a, b);
        Assert.StartsWith("pbkdf2-sha256$100000$", a);
        Assert.True(hasher.Verify("1234", a));
        Assert.False(hasher.Verify("1235", a));
        Assert.False(hasher.Verify("1234", "garbage"));
    }

    /// <summary>The same hash is checked by the till in frontend/packages/domain (pin.test.ts). If either side changes the format, both tests fail.</summary>
    [Fact]
    public void Pbkdf2_verifies_the_hash_the_till_side_also_checks()
    {
        const string sharedVector = "pbkdf2-sha256$100000$AAECAwQFBgcICQoLDA0ODw==$wmpH7CpIGbsLmht6VKVaCLZoFloVcHYAdmA4DgEdAUg=";
        var hasher = new Pbkdf2PinHasher();

        Assert.True(hasher.Verify("4821", sharedVector));
        Assert.False(hasher.Verify("4822", sharedVector));
    }

    [Theory]
    [InlineData("0821234567", "+27821234567")]
    [InlineData("+27 82 123 4567", "+27821234567")]
    [InlineData("0027821234567", "+27821234567")]
    [InlineData("27821234567", "+27821234567")]
    [InlineData("12345", null)]
    [InlineData("", null)]
    public void Normalises_phone_numbers(string input, string? expected) =>
        Assert.Equal(expected, StaffRules.NormalisePhone(input));
}
