using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Vas;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public class VasTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    // ---- rules ----

    [Theory]
    [InlineData(10000, 300, 300)]     // R100 at 3% is R3
    [InlineData(10050, 300, 301)]     // 301.5 rounds down
    [InlineData(2000, 150, 30)]
    [InlineData(500, 0, 0)]
    public void Commission_is_a_share_of_the_amount_rounded_down_to_the_cent(long amount, int bps, long expected) =>
        Assert.Equal(expected, VasRules.CommissionCents(amount, bps));

    [Theory]
    [InlineData(VasProductType.Airtime, "082 123 4567", "+27821234567")]
    [InlineData(VasProductType.Data, "+27 82 123 4567", "+27821234567")]
    [InlineData(VasProductType.Electricity, "0123 4567 890", "01234567890")]
    [InlineData(VasProductType.Electricity, "0123-4567-890", "01234567890")]
    public void Recipients_are_cleaned(VasProductType type, string input, string expected) =>
        Assert.Equal(expected, VasRules.CheckRecipient(type, input).Value);

    [Theory]
    [InlineData(VasProductType.Airtime, "12345")]
    [InlineData(VasProductType.Airtime, "")]
    [InlineData(VasProductType.Electricity, "12345")]
    [InlineData(VasProductType.Electricity, "0123456789012")]
    [InlineData(VasProductType.Electricity, "0821234567")]
    public void A_wrong_phone_or_meter_number_is_refused(VasProductType type, string input) =>
        Assert.Equal("vas.recipient_invalid", VasRules.CheckRecipient(type, input).Error!.Code);

    private static VasSaleInput Input(VasProductType type = VasProductType.Airtime, long amount = 2000, string recipient = "0821234567", string? product = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type, recipient, amount, product, 300, Now);

    [Fact]
    public void Amounts_must_be_whole_rand_inside_the_limits_for_the_product()
    {
        Assert.True(VasSale.Create(Input(amount: 500)).IsSuccess);
        Assert.True(VasSale.Create(Input(amount: 100_000)).IsSuccess);
        Assert.Equal("vas.amount_invalid", VasSale.Create(Input(amount: 499)).Error!.Code);
        Assert.Equal("vas.amount_invalid", VasSale.Create(Input(amount: 100_100)).Error!.Code);
        Assert.Equal("vas.amount_invalid", VasSale.Create(Input(amount: 2050)).Error!.Code);                         // R20.50
        Assert.Equal("vas.amount_invalid", VasSale.Create(Input(VasProductType.Electricity, 1000, "01234567890")).Error!.Code);   // below the R20 electricity minimum
        Assert.True(VasSale.Create(Input(VasProductType.Electricity, 5000, "01234567890")).IsSuccess);
    }

    [Fact]
    public void A_data_sale_needs_a_bundle_and_a_new_sale_is_pending_with_its_commission_worked_out()
    {
        Assert.Equal("vas.product_required", VasSale.Create(Input(VasProductType.Data)).Error!.Code);

        var sale = VasSale.Create(Input(VasProductType.Data, 10000, product: "1GB-30D")).Value!;
        Assert.Equal(VasStatus.Pending, sale.Status);
        Assert.Equal(300, sale.CommissionCents);
        Assert.Equal(0, sale.EarnedCommissionCents);        // nothing earned until it completes
    }

    [Fact]
    public void Commission_is_earned_only_on_a_completed_sale()
    {
        var done = VasSale.Create(Input(amount: 10000)).Value!;
        done.Complete("p-1", null);
        Assert.Equal(300, done.EarnedCommissionCents);

        var failed = VasSale.Create(Input(amount: 10000)).Value!;
        failed.Fail("Declined");
        var unknown = VasSale.Create(Input(amount: 10000)).Value!;
        unknown.MarkUnknown("timeout");
        Assert.Equal(0, failed.EarnedCommissionCents);
        Assert.Equal(0, unknown.EarnedCommissionCents);
    }

    // ---- the handler ----

    private sealed class FakeStore : IVasStore
    {
        public Dictionary<Guid, VasSale> Sales { get; } = [];
        public List<VasStatus> StatusWhenSaved { get; } = [];
        public Task<VasSale?> Get(Guid id, CancellationToken ct) => Task.FromResult(Sales.GetValueOrDefault(id));
        public Task Add(VasSale sale, CancellationToken ct) { Sales[sale.Id] = sale; return Task.CompletedTask; }
        public Task Save(CancellationToken ct) { StatusWhenSaved.AddRange(Sales.Values.Select(s => s.Status)); return Task.CompletedTask; }
    }

    private sealed class FakeAccess : ITillAccessChecker
    {
        public Result Answer { get; set; } = Result.Ok();
        public string? Permission { get; private set; }
        public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct) { Permission = permission; return Task.FromResult(Answer); }
    }

    private sealed class FakePartner(Func<VasPurchase, CancellationToken, Task<VasPartnerResult>> reply) : IVasPartner
    {
        public int Calls { get; private set; }
        public Task<VasPartnerResult> Purchase(VasPurchase p, CancellationToken ct) { Calls++; return reply(p, ct); }
    }

    private readonly FakeStore _store = new();
    private readonly FakeAccess _access = new();
    private readonly Guid _shop = Guid.NewGuid();

    private SellVasHandler Handler(FakePartner? partner, TimeSpan? timeout = null) =>
        new(_store, _access, partner is null ? [] : [partner], VasOptions.Default with { PartnerTimeout = timeout }, new FakeTimeProvider(Now));

    private VasSaleRequest Request(Guid? id = null, string type = "airtime", long amount = 10000, string recipient = "0821234567", string? product = null) =>
        new(id ?? Guid.NewGuid(), _shop, Guid.NewGuid(), Guid.NewGuid(), type, recipient, amount, product);

    private static FakePartner Succeeds(string? token = null) => new((p, _) => Task.FromResult(new VasPartnerResult(VasOutcome.Success, "ref-" + p.Id.ToString("N")[..6], token)));

    [Fact]
    public async Task A_completed_sale_records_the_partner_reference_and_the_commission()
    {
        var partner = Succeeds();
        var result = await Handler(partner).Handle(new SellVas(Request()), default);

        var r = result.Value!;
        Assert.Equal("completed", r.Status);
        Assert.Equal(10000, r.AmountCents);
        Assert.Equal(300, r.CommissionCents);
        Assert.StartsWith("ref-", r.PartnerRef);
        Assert.Null(r.Message);
        Assert.Equal("vas.sell", _access.Permission);
    }

    [Fact]
    public async Task An_electricity_token_is_returned_to_the_cashier_to_give_the_customer()
    {
        var result = await Handler(Succeeds("1111 2222 3333 4444 5555")).Handle(new SellVas(Request(type: "Electricity", amount: 5000, recipient: "0123 4567 890")), default);
        Assert.Equal("1111 2222 3333 4444 5555", result.Value!.Token);
    }

    [Fact]
    public async Task The_sale_is_saved_as_pending_before_the_partner_is_called()
    {
        VasStatus? seenByPartner = null;
        var partner = new FakePartner((p, _) =>
        {
            seenByPartner = _store.Sales[p.Id].Status;
            return Task.FromResult(new VasPartnerResult(VasOutcome.Success, "r"));
        });

        await Handler(partner).Handle(new SellVas(Request()), default);

        Assert.Equal(VasStatus.Pending, seenByPartner);
    }

    [Fact]
    public async Task Sending_the_same_sale_again_returns_the_same_result_and_never_buys_twice()
    {
        var partner = Succeeds();
        var request = Request();

        var first = await Handler(partner).Handle(new SellVas(request), default);
        var again = await Handler(partner).Handle(new SellVas(request), default);

        Assert.Equal(1, partner.Calls);
        Assert.Equal(first.Value!.PartnerRef, again.Value!.PartnerRef);
        Assert.Single(_store.Sales);
    }

    [Fact]
    public async Task A_decline_fails_the_sale_with_no_commission_and_the_reason()
    {
        var partner = new FakePartner((_, _) => Task.FromResult(new VasPartnerResult(VasOutcome.Declined, Reason: "Number not on the network")));

        var r = (await Handler(partner).Handle(new SellVas(Request()), default)).Value!;

        Assert.Equal("failed", r.Status);
        Assert.Equal(0, r.CommissionCents);
        Assert.Equal("Number not on the network", r.Message);
    }

    [Fact]
    public async Task When_the_partner_does_not_answer_the_sale_is_unknown_and_is_not_retried_even_if_sent_again()
    {
        var partner = new FakePartner((_, _) => throw new HttpRequestException("connection reset"));
        var request = Request();

        var first = (await Handler(partner).Handle(new SellVas(request), default)).Value!;
        var again = (await Handler(partner).Handle(new SellVas(request), default)).Value!;

        Assert.Equal("unknown", first.Status);
        Assert.Contains("Do not sell it again", first.Message);
        Assert.Equal("unknown", again.Status);
        Assert.Equal(1, partner.Calls);
        Assert.Equal(0, first.CommissionCents);
    }

    [Fact]
    public async Task A_partner_that_takes_too_long_is_cut_off_and_the_sale_is_unknown()
    {
        var partner = new FakePartner(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return new VasPartnerResult(VasOutcome.Success, "late"); });

        var r = (await Handler(partner, timeout: TimeSpan.FromMilliseconds(50)).Handle(new SellVas(Request()), default)).Value!;

        Assert.Equal("unknown", r.Status);
    }

    [Fact]
    public async Task Nothing_is_recorded_when_VAS_is_unavailable_the_caller_may_not_sell_or_the_sale_is_invalid()
    {
        Assert.Equal("vas.unavailable", (await Handler(null).Handle(new SellVas(Request()), default)).Error!.Code);

        _access.Answer = Result.Fail("permission.denied", "no");
        Assert.Equal("permission.denied", (await Handler(Succeeds()).Handle(new SellVas(Request()), default)).Error!.Code);
        _access.Answer = Result.Ok();

        Assert.Equal("vas.product_invalid", (await Handler(Succeeds()).Handle(new SellVas(Request(type: "lottery")), default)).Error!.Code);
        Assert.Equal("vas.amount_invalid", (await Handler(Succeeds()).Handle(new SellVas(Request(amount: 1)), default)).Error!.Code);
        Assert.Equal("vas.recipient_invalid", (await Handler(Succeeds()).Handle(new SellVas(Request(recipient: "12")), default)).Error!.Code);
        Assert.Empty(_store.Sales);
    }

    [Fact]
    public async Task A_sale_id_from_another_shop_is_refused()
    {
        var request = Request();
        await Handler(Succeeds()).Handle(new SellVas(request), default);

        var other = await Handler(Succeeds()).Handle(new SellVas(request with { ShopId = Guid.NewGuid() }), default);

        Assert.Equal("vas.id_taken", other.Error!.Code);
    }
}
