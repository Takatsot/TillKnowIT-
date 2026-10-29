using Customers.Application;
using Customers.Contracts;
using Customers.Domain;
using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using Platform.Contracts;
using ShopCore.Contracts;
using TillKnowIT.SharedKernel;

namespace Customers.Application.Tests;

public class SaPhoneTests
{
    [Theory]
    [InlineData("0821234567", "+27821234567")]
    [InlineData("082 123 4567", "+27821234567")]
    [InlineData("+27 82 123 4567", "+27821234567")]
    [InlineData("27821234567", "+27821234567")]
    [InlineData("0027821234567", "+27821234567")]
    [InlineData("(011) 555-1234", "+27115551234")]
    public void Every_way_of_writing_a_south_african_number_is_the_same_number(string raw, string expected) =>
        Assert.Equal(expected, SaPhone.Normalise(raw));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("08212345")]
    [InlineData("08212345678")]
    [InlineData("+44 7911 123456")]
    [InlineData("abc")]
    public void Anything_else_is_not_a_phone_number(string? raw) => Assert.Null(SaPhone.Normalise(raw));
}

public class CustomerDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static Customer Make(bool consent = false) =>
        Customer.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new("Mrs Dlamini", "082 123 4567", null, consent, "till"), Now).Value!;

    [Fact]
    public void A_customer_is_kept_with_a_normalised_phone_and_no_consent_unless_given()
    {
        var customer = Make();

        Assert.Equal(("Mrs Dlamini", "+27821234567", false), (customer.Name, customer.Phone, customer.MessageConsent));
        Assert.Equal("customer.name_invalid", Customer.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, new(" ", "0821234567", null, false, null), Now).Error!.Code);
        Assert.Equal("customer.phone_invalid", Customer.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, new("A", "123", null, false, null), Now).Error!.Code);
        Assert.Equal("customer.email_invalid", Customer.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, new("A", "0821234567", "not an email", false, null), Now).Error!.Code);
    }

    [Fact]
    public void Consent_records_when_and_how_it_was_given_and_an_update_that_leaves_it_alone_keeps_that()
    {
        var customer = Make(consent: true);
        var given = customer.ConsentAt;

        customer.Update(new("Mrs D Dlamini", "0821234567", "d@example.com", true, "till"), Now.AddDays(10));

        Assert.Equal((given, "Mrs D Dlamini"), (customer.ConsentAt, customer.Name));
        Assert.Equal("till", customer.ConsentSource);
    }

    [Fact]
    public void Withdrawing_consent_is_remembered_and_giving_it_again_sets_a_new_date()
    {
        var customer = Make(consent: true);

        customer.SetConsent(false, null, Now.AddDays(1));
        Assert.Equal((false, Now.AddDays(1)), (customer.MessageConsent, customer.ConsentAt));

        customer.SetConsent(true, "whatsapp", Now.AddDays(2));
        Assert.Equal((true, "whatsapp", Now.AddDays(2)), (customer.MessageConsent, customer.ConsentSource, customer.ConsentAt!.Value));

        var version = customer.Version;
        customer.SetConsent(true, "till", Now.AddDays(3));   // already yes: nothing changes
        Assert.Equal((version, Now.AddDays(2)), (customer.Version, customer.ConsentAt!.Value));
    }

    [Fact]
    public void A_merged_customer_cannot_be_edited_and_its_phone_is_free()
    {
        var customer = Make();
        customer.MergeInto(Guid.NewGuid());

        Assert.True(customer.IsMerged);
        Assert.Equal("customer.merged", customer.Update(new("X", "0821234567", null, false, null), Now).Error!.Code);
    }
}

public class LoyaltyRulesTests
{
    [Fact]
    public void Whole_rands_earn_points_at_the_shops_rate()
    {
        var rules = new LoyaltyRules();

        Assert.Equal(49, rules.Earned(4_999));
        Assert.Equal(0, rules.Earned(99));
        Assert.Equal(0, rules.Earned(-500));
        Assert.Equal(98, new LoyaltyRules(PointsPerRand: 2).Earned(4_999));
        Assert.Equal(0, new LoyaltyRules(PointsPerRand: 0).Earned(10_000));
    }

    [Fact]
    public void Points_are_worth_what_the_shop_says()
    {
        Assert.Equal(1_000, new LoyaltyRules().ValueCents(100));
        Assert.Equal(2_500, new LoyaltyRules(CentsPerPoint: 25).ValueCents(100));
    }

    [Fact]
    public void A_refund_takes_back_the_same_share_of_points_as_of_money_rounded_down()
    {
        Assert.Equal(50, LoyaltyRules.ShareOf(100, 5_000, 10_000));
        Assert.Equal(33, LoyaltyRules.ShareOf(100, 3_333, 10_000));
        Assert.Equal(100, LoyaltyRules.ShareOf(100, 10_000, 10_000));
        Assert.Equal(100, LoyaltyRules.ShareOf(100, 20_000, 10_000));   // never more than was earned
        Assert.Equal(0, LoyaltyRules.ShareOf(100, 5_000, 0));
        Assert.Equal(0, LoyaltyRules.ShareOf(0, 5_000, 10_000));
    }

    [Fact]
    public void Rules_come_from_the_pack_settings_with_defaults_for_anything_missing_or_silly()
    {
        Assert.Equal(new LoyaltyRules(), LoyaltyRules.FromJson(null));
        Assert.Equal(new LoyaltyRules(2, 5, 50), LoyaltyRules.FromJson("{\"pointsPerRand\":2,\"centsPerPoint\":5,\"minRedeemPoints\":50}"));
        Assert.Equal(new LoyaltyRules(1, 10, 100), LoyaltyRules.FromJson("{\"pointsPerRand\":-1,\"centsPerPoint\":0,\"minRedeemPoints\":0}"));
        Assert.Equal(new LoyaltyRules(), LoyaltyRules.FromJson("nonsense"));
    }
}

public class CustomerFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class Store : ICustomerStore
    {
        public List<Customer> Customers { get; } = [];
        public List<LoyaltyEntry> Entries { get; } = [];
        public int Saves { get; private set; }
        public void Add(Customer c) => Customers.Add(c);
        public Task<Customer?> Get(Guid shopId, Guid id, CancellationToken ct) => Task.FromResult(Customers.FirstOrDefault(c => c.ShopId == shopId && c.Id == id));
        public Task<Customer?> ByPhone(Guid shopId, string phone, CancellationToken ct) => Task.FromResult(Customers.FirstOrDefault(c => c.ShopId == shopId && c.Phone == phone && !c.IsMerged));
        public Task<List<Customer>> Search(Guid shopId, string text, int take, CancellationToken ct) =>
            Task.FromResult(Customers.Where(c => c.ShopId == shopId && !c.IsMerged && (c.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || c.Phone == SaPhone.Normalise(text) || c.Phone.Contains(new string(text.Where(char.IsDigit).ToArray())))).Take(take).ToList());
        public void Add(LoyaltyEntry e) => Entries.Add(e);
        public Task<LoyaltyEntry?> EntryByReference(Guid customerId, string reference, CancellationToken ct) => Task.FromResult(Entries.FirstOrDefault(e => e.CustomerId == customerId && e.Reference == reference));
        public Task<List<LoyaltyEntry>> EntriesForSale(Guid saleId, CancellationToken ct) => Task.FromResult(Entries.Where(e => e.SaleId == saleId).ToList());
        public Task<int> Balance(Guid customerId, CancellationToken ct) => Task.FromResult(Entries.Where(e => e.CustomerId == customerId).Sum(e => e.Points));
        public Task<Dictionary<Guid, int>> Balances(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult(Entries.Where(e => ids.Contains(e.CustomerId)).GroupBy(e => e.CustomerId).ToDictionary(g => g.Key, g => g.Sum(e => e.Points)));
        public Task<List<LoyaltyEntry>> Recent(Guid customerId, int take, CancellationToken ct) => Task.FromResult(Entries.Where(e => e.CustomerId == customerId).OrderByDescending(e => e.CreatedAt).Take(take).ToList());
        public Task Save(CancellationToken ct) { Saves++; return Task.CompletedTask; }
    }

    private sealed class Tills : ITillAccessChecker
    {
        public HashSet<(Guid Device, Guid Staff)> Allowed { get; } = [];
        public HashSet<(Guid Staff, string Permission)> Denied { get; } = [];
        public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct) =>
            Task.FromResult(!Allowed.Contains((deviceId, staffId)) ? Result.Fail("device.not_approved", "No.") : Denied.Contains((staffId, permission)) ? Result.Fail("permission.denied", "No.") : Result.Ok());
    }

    private sealed class People : IOrganisationAccess
    {
        public HashSet<Guid> Owners { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Owners.Contains(p));
    }

    private sealed class Features : IShopFeatures
    {
        public HashSet<string> Off { get; } = [];
        public string? LoyaltySettings { get; set; }
        public Task<bool> IsOn(Guid shopId, string feature, CancellationToken ct) => Task.FromResult(!Off.Contains(feature));
        public Task<ShopPackResponse> For(Guid shopId, CancellationToken ct) =>
            Task.FromResult(new ShopPackResponse(shopId, "test", "Test", 1, "slip.sale", [new FeatureDto("loyalty", LoyaltySettings), new FeatureDto("customers", null)], "Test shop"));
    }

    private readonly Store _store = new();
    private readonly Tills _tills = new();
    private readonly People _people = new();
    private readonly Features _features = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _device = Guid.NewGuid();
    private readonly Guid _cashier = Guid.NewGuid();
    private readonly Guid _manager = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();

    public CustomerFlowTests()
    {
        _tills.Allowed.Add((_device, _cashier));
        _tills.Allowed.Add((_device, _manager));
        _people.Owners.Add(_owner);
    }

    private CustomerHandlers Customers => new(_store, _tills, _people, _features, _clock);
    private LoyaltyHandlers Loyalty => new(_store, _tills, _features, _clock);

    private Task<Result<CustomerResponse>> Add(string name = "Mrs Dlamini", string phone = "0821234567", Guid? id = null, bool consent = false) =>
        Customers.Handle(new CreateCustomer(_shop, new(_device, _cashier, id ?? Guid.NewGuid(), name, phone, null, consent, null)), default);

    // ---- customers ----

    [Fact]
    public async Task A_customer_is_added_in_seconds_with_a_name_and_a_phone_and_found_again_by_either()
    {
        var customer = (await Add()).Value!;

        Assert.Equal(("+27821234567", false, 0), (customer.Phone, customer.MessageConsent, customer.Points));
        Assert.Equal(customer.Id, (await Customers.Handle(new FindCustomers(_shop, _device, _cashier, "0821234567"), default)).Value!.Single().Id);
        Assert.Equal(customer.Id, (await Customers.Handle(new FindCustomers(_shop, _device, _cashier, "dlamin"), default)).Value!.Single().Id);
        Assert.Equal(customer.Id, (await Customers.Handle(new GetCustomer(_shop, customer.Id, _device, _cashier), default)).Value!.Id);
    }

    [Fact]
    public async Task Adding_someone_whose_phone_is_already_on_the_list_gives_back_that_person_and_so_does_sending_the_same_request_twice()
    {
        var first = (await Add()).Value!;

        var samePhone = (await Add("Someone Else", "+27 82 123 4567")).Value!;
        var sameId = (await Add("Another", "0839999999", first.Id)).Value!;

        Assert.Equal((first.Id, first.Id), (samePhone.Id, sameId.Id));
        Assert.Equal("Mrs Dlamini", samePhone.Name);
        Assert.Single(_store.Customers);
    }

    [Fact]
    public async Task A_customer_needs_a_name_and_a_real_number_an_approved_till_and_the_pack_feature()
    {
        Assert.Equal("customer.phone_invalid", (await Add("A", "12")).Error!.Code);
        Assert.Equal("customer.name_invalid", (await Add(" ")).Error!.Code);
        Assert.Equal("device.not_approved", (await Customers.Handle(new CreateCustomer(_shop, new(Guid.NewGuid(), _cashier, Guid.NewGuid(), "A", "0821234567", null, false, null)), default)).Error!.Code);
        _features.Off.Add("customers");
        Assert.Equal("feature.not_enabled", (await Add()).Error!.Code);
        Assert.Empty(_store.Customers);
    }

    [Fact]
    public async Task Updating_changes_the_details_but_not_to_a_number_that_belongs_to_someone_else_and_searching_needs_a_few_characters()
    {
        var a = (await Add("Anna", "0821111111")).Value!;
        await Add("Ben", "0822222222");

        var updated = await Customers.Handle(new UpdateCustomer(_shop, a.Id, new(_device, _cashier, "Anna Smith", "0821111111", null, true, "whatsapp")), default);
        Assert.Equal(("Anna Smith", true), (updated.Value!.Name, updated.Value.MessageConsent));
        Assert.Equal("customer.phone_taken", (await Customers.Handle(new UpdateCustomer(_shop, a.Id, new(_device, _cashier, "Anna", "0822222222", null, true, null)), default)).Error!.Code);
        Assert.Equal("customer.not_found", (await Customers.Handle(new UpdateCustomer(_shop, Guid.NewGuid(), new(_device, _cashier, "X", "0821111111", null, false, null)), default)).Error!.Code);
        Assert.Equal("customer.search_short", (await Customers.Handle(new FindCustomers(_shop, _device, _cashier, "an"), default)).Error!.Code);
    }

    [Fact]
    public async Task A_customer_of_another_shop_is_not_found()
    {
        var customer = (await Add()).Value!;

        Assert.Equal("customer.not_found", (await Customers.Handle(new GetCustomer(Guid.NewGuid(), customer.Id, _device, _cashier), default)).Error!.Code);
    }

    // ---- merging ----

    [Fact]
    public async Task Merging_moves_the_points_as_entries_frees_the_phone_and_keeps_the_old_record_on_file()
    {
        var keep = (await Add("Anna Smith", "0821111111")).Value!;
        var dup = (await Add("Anna S", "0822222222")).Value!;
        _store.Entries.Add(new LoyaltyEntry(dup.Id, _shop, LoyaltyEntryType.Earn, 120, "sale:x", null, null, null, Now));
        _store.Entries.Add(new LoyaltyEntry(keep.Id, _shop, LoyaltyEntryType.Earn, 30, "sale:y", null, null, null, Now));

        var merged = (await Customers.Handle(new MergeCustomers(_shop, keep.Id, new(_owner, dup.Id)), default)).Value!;

        Assert.Equal(150, merged.Points);
        Assert.Equal(0, await _store.Balance(dup.Id, default));
        Assert.True(_store.Customers.Single(c => c.Id == dup.Id).IsMerged);
        Assert.Equal(4, _store.Entries.Count);   // two originals and the two merge entries: nothing edited or deleted
        Assert.Null(await _store.ByPhone(_shop, "+27822222222", default));
    }

    [Fact]
    public async Task Only_the_owner_merges_and_not_a_customer_with_itself_or_one_already_merged()
    {
        var a = (await Add("Anna", "0821111111")).Value!;
        var b = (await Add("Ben", "0822222222")).Value!;
        var c = (await Add("Cara", "0823333333")).Value!;

        Assert.Equal("permission.denied", (await Customers.Handle(new MergeCustomers(_shop, a.Id, new(Guid.NewGuid(), b.Id)), default)).Error!.Code);
        Assert.Equal("customer.merge_self", (await Customers.Handle(new MergeCustomers(_shop, a.Id, new(_owner, a.Id)), default)).Error!.Code);
        Assert.Equal("customer.not_found", (await Customers.Handle(new MergeCustomers(_shop, a.Id, new(_owner, Guid.NewGuid())), default)).Error!.Code);
        await Customers.Handle(new MergeCustomers(_shop, a.Id, new(_owner, b.Id)), default);
        Assert.Equal("customer.merged", (await Customers.Handle(new MergeCustomers(_shop, c.Id, new(_owner, b.Id)), default)).Error!.Code);
    }

    [Fact]
    public async Task The_directory_follows_a_merge_and_says_whether_the_customer_agreed_to_be_messaged()
    {
        var keep = (await Add("Anna", "0821111111", consent: true)).Value!;
        var dup = (await Add("Anna S", "0822222222")).Value!;
        await Customers.Handle(new MergeCustomers(_shop, keep.Id, new(_owner, dup.Id)), default);

        var info = await new CustomerDirectory(_store).Get(_shop, dup.Id, default);

        Assert.Equal((keep.Id, "+27821111111", true), (info!.Id, info.Phone, info.MessageConsent));
        Assert.Null(await new CustomerDirectory(_store).Get(_shop, Guid.NewGuid(), default));
    }

    // ---- points ----

    private async Task<Guid> CustomerWithPoints(int points)
    {
        var customer = (await Add()).Value!;
        _store.Entries.Add(new LoyaltyEntry(customer.Id, _shop, LoyaltyEntryType.Earn, points, $"seed:{Guid.NewGuid():N}", null, null, null, Now));
        return customer.Id;
    }

    [Fact]
    public async Task A_customers_points_and_what_they_are_worth_are_read_from_their_entries()
    {
        var id = await CustomerWithPoints(250);

        var points = (await Loyalty.Handle(new GetPoints(_shop, id, _device, _cashier), default)).Value!;

        Assert.Equal((250, 2_500L, 100), (points.Points, points.ValueCents, points.MinRedeemPoints));
        Assert.Single(points.Recent);
    }

    [Fact]
    public async Task Redeeming_takes_points_off_and_gives_back_their_value_and_repeating_it_changes_nothing()
    {
        var id = await CustomerWithPoints(250);

        var first = (await Loyalty.Handle(new RedeemPoints(_shop, id, new(_device, _cashier, 100, "sale-9")), default)).Value!;
        var again = (await Loyalty.Handle(new RedeemPoints(_shop, id, new(_device, _cashier, 100, "sale-9")), default)).Value!;

        Assert.Equal((100, 1_000L, 150), (first.Points, first.ValueCents, first.BalanceAfter));
        Assert.Equal((first.RedemptionReference, 150), (again.RedemptionReference, again.BalanceAfter));
        Assert.Equal(150, await _store.Balance(id, default));
    }

    [Fact]
    public async Task Redeeming_needs_enough_points_and_the_minimum_and_a_reference()
    {
        var id = await CustomerWithPoints(250);

        Assert.Equal("loyalty.below_minimum", (await Loyalty.Handle(new RedeemPoints(_shop, id, new(_device, _cashier, 99, "r1")), default)).Error!.Code);
        Assert.Equal("loyalty.insufficient", (await Loyalty.Handle(new RedeemPoints(_shop, id, new(_device, _cashier, 251, "r2")), default)).Error!.Code);
        Assert.Equal("loyalty.reference_required", (await Loyalty.Handle(new RedeemPoints(_shop, id, new(_device, _cashier, 100, " ")), default)).Error!.Code);
        Assert.Equal(250, await _store.Balance(id, default));
    }

    [Fact]
    public async Task The_shops_own_rules_set_the_minimum_and_the_value()
    {
        _features.LoyaltySettings = "{\"centsPerPoint\":25,\"minRedeemPoints\":20}";
        var id = await CustomerWithPoints(100);

        var done = (await Loyalty.Handle(new RedeemPoints(_shop, id, new(_device, _cashier, 20, "r")), default)).Value!;

        Assert.Equal(500, done.ValueCents);
    }

    [Fact]
    public async Task A_manager_adjusts_points_with_a_reason_and_never_below_zero_and_a_cashier_cannot()
    {
        var id = await CustomerWithPoints(100);

        Assert.True((await Loyalty.Handle(new AdjustPoints(_shop, id, new(_device, _manager, _cashier, 50, "Missed sale")), default)).IsSuccess);
        Assert.Equal(150, await _store.Balance(id, default));
        Assert.Equal("loyalty.insufficient", (await Loyalty.Handle(new AdjustPoints(_shop, id, new(_device, _manager, _cashier, -151, "Oops")), default)).Error!.Code);
        Assert.Equal("loyalty.reason_required", (await Loyalty.Handle(new AdjustPoints(_shop, id, new(_device, _manager, _cashier, 5, " ")), default)).Error!.Code);
        Assert.Equal("loyalty.adjustment_invalid", (await Loyalty.Handle(new AdjustPoints(_shop, id, new(_device, _manager, _cashier, 0, "Nothing")), default)).Error!.Code);

        _tills.Denied.Add((_manager, "sale.refund_approve"));   // the person named as approving may not
        Assert.Equal("loyalty.approver_not_allowed", (await Loyalty.Handle(new AdjustPoints(_shop, id, new(_device, _manager, _cashier, 5, "Because")), default)).Error!.Code);
    }

    [Fact]
    public async Task Loyalty_needs_its_pack_feature()
    {
        var id = await CustomerWithPoints(100);
        _features.Off.Add("loyalty");

        Assert.Equal("feature.not_enabled", (await Loyalty.Handle(new GetPoints(_shop, id, _device, _cashier), default)).Error!.Code);
        Assert.Equal("feature.not_enabled", (await Loyalty.Handle(new RedeemPoints(_shop, id, new(_device, _cashier, 100, "r")), default)).Error!.Code);
    }

    // ---- earning and taking back ----

    [Fact]
    public async Task A_sale_for_a_customer_earns_points_once_even_if_the_event_arrives_twice()
    {
        var customer = (await Add()).Value!;
        var sale = new SaleRecorded(Guid.NewGuid(), _shop, customer.Id, 4_999, Now);
        var handler = new EarnPointsOnSale(_store, _features);

        await handler.Handle(sale, default);
        await handler.Handle(sale, default);

        Assert.Equal(49, await _store.Balance(customer.Id, default));
        Assert.Single(_store.Entries);
    }

    [Fact]
    public async Task Points_go_to_the_customer_that_carries_on_after_a_merge_and_nothing_is_earned_for_shops_without_loyalty()
    {
        var keep = (await Add("Anna", "0821111111")).Value!;
        var dup = (await Add("Anna S", "0822222222")).Value!;
        await Customers.Handle(new MergeCustomers(_shop, keep.Id, new(_owner, dup.Id)), default);

        await new EarnPointsOnSale(_store, _features).Handle(new SaleRecorded(Guid.NewGuid(), _shop, dup.Id, 10_000, Now), default);
        Assert.Equal(100, await _store.Balance(keep.Id, default));

        _features.Off.Add("loyalty");
        await new EarnPointsOnSale(_store, _features).Handle(new SaleRecorded(Guid.NewGuid(), _shop, keep.Id, 10_000, Now), default);
        Assert.Equal(100, await _store.Balance(keep.Id, default));
    }

    [Fact]
    public async Task A_cancelled_sale_takes_its_points_back_once()
    {
        var customer = (await Add()).Value!;
        var saleId = Guid.NewGuid();
        await new EarnPointsOnSale(_store, _features).Handle(new SaleRecorded(saleId, _shop, customer.Id, 10_000, Now), default);
        var handler = new ReversePointsOnCancel(_store, _clock);

        await handler.Handle(new SaleCancelled(saleId, _shop), default);
        await handler.Handle(new SaleCancelled(saleId, _shop), default);

        Assert.Equal(0, await _store.Balance(customer.Id, default));
        Assert.Equal(2, _store.Entries.Count);
    }

    [Fact]
    public async Task A_refund_takes_back_a_share_of_the_points_and_each_refund_only_once()
    {
        var customer = (await Add()).Value!;
        var saleId = Guid.NewGuid();
        await new EarnPointsOnSale(_store, _features).Handle(new SaleRecorded(saleId, _shop, customer.Id, 10_000, Now), default);   // 100 points
        var handler = new ReversePointsOnRefund(_store, _clock);
        var refund = new SaleRefunded(Guid.NewGuid(), saleId, _shop, 2_500, 10_000);

        await handler.Handle(refund, default);
        await handler.Handle(refund, default);
        await handler.Handle(new SaleRefunded(Guid.NewGuid(), saleId, _shop, 2_500, 10_000), default);

        Assert.Equal(50, await _store.Balance(customer.Id, default));   // 25 back, twice
    }

    [Fact]
    public async Task Reversals_for_a_sale_nobody_earned_for_do_nothing()
    {
        await new ReversePointsOnCancel(_store, _clock).Handle(new SaleCancelled(Guid.NewGuid(), _shop), default);
        await new ReversePointsOnRefund(_store, _clock).Handle(new SaleRefunded(Guid.NewGuid(), Guid.NewGuid(), _shop, 100, 1_000), default);

        Assert.Empty(_store.Entries);
    }
}
