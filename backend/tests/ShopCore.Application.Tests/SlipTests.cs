using System.Security.Cryptography;
using Identity.Contracts;
using Messaging.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Slips;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public class SlipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 4, 0, TimeSpan.Zero);   // 12:04 in the shop
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    // ---- token ----

    [Fact]
    public void A_token_reads_back_to_the_sale_it_was_made_for()
    {
        var id = Guid.NewGuid();
        var token = SlipToken.Create(id, Key);

        Assert.Equal(43, token.Length);
        Assert.True(SlipToken.TryRead(token, Key, out var read));
        Assert.Equal(id, read);
        Assert.DoesNotContain('=', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
    }

    [Fact]
    public void Altered_foreign_or_malformed_tokens_are_rejected()
    {
        var token = SlipToken.Create(Guid.NewGuid(), Key);
        var flipped = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.False(SlipToken.TryRead(flipped, Key, out _));
        Assert.False(SlipToken.TryRead(token, RandomNumberGenerator.GetBytes(32), out _));
        Assert.False(SlipToken.TryRead(SlipToken.Create(Guid.NewGuid(), Key)[..40], Key, out _));
        Assert.False(SlipToken.TryRead(Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('='), Key, out _));   // a bare sale id with no signature
        Assert.False(SlipToken.TryRead(new string('!', 43), Key, out _));
        Assert.False(SlipToken.TryRead(null, Key, out _));
    }

    [Fact]
    public void Another_sales_token_does_not_open_this_sale()
    {
        var a = SlipToken.Create(Guid.NewGuid(), Key);
        var b = SlipToken.Create(Guid.NewGuid(), Key);
        SlipToken.TryRead(a, Key, out var idA);
        SlipToken.TryRead(b, Key, out var idB);
        Assert.NotEqual(idA, idB);
    }

    // ---- formatting ----

    [Fact]
    public void The_slip_reads_like_a_till_slip_in_shop_time()
    {
        var lines = new[] { new SlipLine("Bread", 2, 1800), new SlipLine("Milk 1l", 1, 2500) };
        var values = SlipFormatter.Values("Mama's Spaza", lines, 6100, Now, "https://s/x");

        Assert.Equal("2 x Bread  R36.00\n1 x Milk 1l  R25.00", values["lines"]);
        Assert.Equal("R61.00", values["total"]);
        Assert.Equal("26 Oct 2026 12:04", values["date"]);
        Assert.Equal(["shop", "lines", "total", "date", "url"], values.Keys);
    }

    // ---- fakes ----

    private sealed class FakeStore : ISlipStore
    {
        public Dictionary<Guid, SlipSale> Sales { get; } = [];
        public List<SlipRequest> Requests { get; } = [];
        public Task<SlipSale?> GetSale(Guid saleId, CancellationToken ct) => Task.FromResult(Sales.GetValueOrDefault(saleId));
        public Task<int> CountRequests(Guid saleId, CancellationToken ct) => Task.FromResult(Requests.Count(r => r.SaleId == saleId));
        public Task Add(SlipRequest slip, CancellationToken ct) { Requests.Add(slip); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeAccess : ITillAccessChecker
    {
        public Result Answer { get; set; } = Result.Ok();
        public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct)
        {
            LastPermission = permission;
            return Task.FromResult(Answer);
        }
        public string? LastPermission { get; private set; }
    }

    private sealed class FakeShops : IShopDirectory
    {
        public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) => Task.FromResult<ShopInfo?>(new ShopInfo(shopId, "Mama's Spaza", "verified", null, null, null, "en"));
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(new List<Guid>());
    }

    private sealed class FakeDestinations : IDestinationChecker
    {
        public Result<string> Check(string channel, string? destination) =>
            destination is { Length: > 5 } ? Result<string>.Ok("+27" + destination.TrimStart('0')) : Result<string>.Fail("destination.phone_invalid", "bad");
    }

    private readonly FakeStore _store = new();
    private readonly FakeAccess _access = new();
    private readonly Guid _shop = Guid.NewGuid();
    private readonly SlipSale _sale;
    private readonly SlipOptions _options = new(Key, "https://slip.example/s/");

    public SlipTests()
    {
        _sale = new SlipSale(Guid.NewGuid(), _shop, Now, 3600, 470, "cash", false, [new SlipLine("Bread", 2, 1800)]);
        _store.Sales[_sale.SaleId] = _sale;
    }

    private RequestSlipHandler Handler() => new(_store, _access, new FakeShops(), new FakeDestinations(), _options, new FakeTimeProvider(Now));

    private RequestSlip Ask(string channel, string? destination = "0821234567", Guid? shop = null) =>
        new(_sale.SaleId, new RequestSlipRequest(shop ?? _shop, Guid.NewGuid(), Guid.NewGuid(), channel, destination, "zu"));

    // ---- request a slip ----

    [Fact]
    public async Task A_qr_slip_is_free_and_just_returns_the_link()
    {
        var result = await Handler().Handle(Ask("qr", destination: null), default);

        Assert.Equal("shown", result.Value!.Status);
        Assert.StartsWith("https://slip.example/s/", result.Value.Url);
        var request = Assert.Single(_store.Requests);
        Assert.Equal("shown", request.Status);
        Assert.Empty(request.DomainEvents);                      // nothing for Messaging to send
        Assert.True(SlipToken.TryRead(result.Value.Url.Split('/')[^1], Key, out var id));
        Assert.Equal(_sale.SaleId, id);
    }

    [Fact]
    public async Task A_message_slip_raises_the_event_Messaging_turns_into_a_whatsapp_sms_or_email()
    {
        var result = await Handler().Handle(Ask("whatsapp"), default);

        Assert.Equal("queued", result.Value!.Status);
        var request = Assert.Single(_store.Requests);
        var e = Assert.IsType<SlipRequested>(Assert.Single(request.DomainEvents));
        Assert.Equal("whatsapp", e.Channel);
        Assert.Equal("+27821234567", e.Destination);              // the cleaned number, not what was typed
        Assert.Equal("zu", e.Language);
        Assert.Equal("Mama's Spaza", e.Values["shop"]);
        Assert.Equal("R36.00", e.Values["total"]);
        Assert.Equal(result.Value.Url, e.Values["url"]);
    }

    [Fact]
    public async Task Slips_need_the_slip_send_permission_and_a_known_channel()
    {
        await Handler().Handle(Ask("qr", null), default);
        Assert.Equal("slip.send", _access.LastPermission);

        _access.Answer = Result.Fail("permission.denied", "no");
        Assert.Equal("permission.denied", (await Handler().Handle(Ask("sms"), default)).Error!.Code);
        _access.Answer = Result.Ok();
        Assert.Equal("slip.channel_invalid", (await Handler().Handle(Ask("fax"), default)).Error!.Code);
    }

    [Fact]
    public async Task A_bad_number_is_refused_at_the_till_so_the_cashier_can_fix_it()
    {
        var result = await Handler().Handle(Ask("sms", destination: "123"), default);

        Assert.Equal("destination.phone_invalid", result.Error!.Code);
        Assert.Empty(_store.Requests);
    }

    [Fact]
    public async Task A_shop_cannot_send_a_slip_for_another_shops_sale_or_an_unknown_sale()
    {
        Assert.Equal("sale.not_found", (await Handler().Handle(Ask("qr", null, shop: Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("sale.not_found", (await Handler().Handle(new RequestSlip(Guid.NewGuid(), Ask("qr", null).Request), default)).Error!.Code);
    }

    [Fact]
    public async Task A_sale_can_have_at_most_three_slips_so_the_button_cannot_spam_someone()
    {
        for (var i = 0; i < 3; i++) Assert.True((await Handler().Handle(Ask("sms"), default)).IsSuccess);

        var fourth = await Handler().Handle(Ask("sms"), default);

        Assert.Equal("slip.limit_reached", fourth.Error!.Code);
        Assert.Equal(3, _store.Requests.Count);
    }

    // ---- the public slip ----

    private GetSlipHandler Public() => new(_store, new FakeShops(), _options);

    [Fact]
    public async Task The_link_shows_the_slip_and_only_the_slip()
    {
        var token = SlipToken.Create(_sale.SaleId, Key);

        var view = (await Public().Handle(new GetSlip(token), default)).Value!;

        Assert.Equal("Mama's Spaza", view.ShopName);
        Assert.Equal("26 Oct 2026 12:04", view.Date);
        Assert.Equal(3600, view.TotalCents);
        Assert.Equal(470, view.VatCents);
        Assert.Equal("cash", view.PaymentMethod);
        Assert.False(view.Cancelled);
        Assert.Equal([new SlipLineView("Bread", 2, 1800)], view.Lines);
        // No staff, device or customer ids exist on the view at all.
        Assert.DoesNotContain(typeof(SlipView).GetProperties(), p => p.Name.Contains("Staff") || p.Name.Contains("Device") || p.Name.Contains("Account"));
    }

    [Fact]
    public async Task A_cancelled_sale_says_so_on_its_slip()
    {
        _store.Sales[_sale.SaleId] = _sale with { Cancelled = true };
        var view = (await Public().Handle(new GetSlip(SlipToken.Create(_sale.SaleId, Key)), default)).Value!;
        Assert.True(view.Cancelled);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    public async Task Bad_tokens_and_unknown_sales_look_exactly_alike(string token)
    {
        var bad = await Public().Handle(new GetSlip(token), default);
        var unknown = await Public().Handle(new GetSlip(SlipToken.Create(Guid.NewGuid(), Key)), default);

        Assert.Equal("slip.not_found", bad.Error!.Code);
        Assert.Equal(bad.Error.Code, unknown.Error!.Code);
        Assert.Equal(bad.Error.Message, unknown.Error.Message);
    }
}
