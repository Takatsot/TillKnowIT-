using Identity.Application.Auth;
using Identity.Contracts;
using Identity.Domain;
using Messaging.Contracts;
using Microsoft.Extensions.Time.Testing;
using TillKnowIT.Security;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Tests;

public class OtpTests
{
    private sealed class Store : IAuthStore
    {
        public List<PersonRef> People { get; } = [];
        public List<OtpChallenge> Challenges { get; } = [];
        public Task<PersonRef?> PersonByPhone(string key, CancellationToken ct) => Task.FromResult(People.Count(p => OtpHandlers.PhoneKey(p.Phone) == key) == 1 ? People.First(p => OtpHandlers.PhoneKey(p.Phone) == key) : null);
        public void Add(OtpChallenge c) => Challenges.Add(c);
        public Task<OtpChallenge?> LatestOpen(string key, DateTimeOffset now, CancellationToken ct) => Task.FromResult(Challenges.Where(c => c.PhoneKey == key && c.IsOpen(now)).OrderByDescending(c => c.CreatedAt).FirstOrDefault());
        public Task<int> RequestedSince(string key, DateTimeOffset since, CancellationToken ct) => Task.FromResult(Challenges.Count(c => c.PhoneKey == key && c.CreatedAt >= since));
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Sms : IMessageQueue
    {
        public List<QueueMessageRequest> Sent { get; } = [];
        public Task<Result<QueueMessageResult>> Queue(QueueMessageRequest r, CancellationToken ct) { Sent.Add(r); return Task.FromResult(Result<QueueMessageResult>.Ok(new(Guid.NewGuid(), false))); }
    }

    private sealed class Phones : IDestinationChecker
    {
        public Result<string> Check(string channel, string? destination)
        {
            var digits = new string((destination ?? "").Where(char.IsDigit).ToArray());
            return digits.Length >= 9 ? Result<string>.Ok("+27" + digits[^9..]) : Result<string>.Fail("destination.phone_invalid", "bad");
        }
    }

    private const string Key = "a-test-key-that-is-at-least-32-characters-long";
    private readonly Store _store = new();
    private readonly Sms _sms = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
    private readonly PersonRef _thandi = new(Guid.NewGuid(), "Thandi", "082 123 4567", "zu");

    public OtpTests() => _store.People.Add(_thandi);

    private OtpHandlers Handlers(bool dev = true) => new(_store, _sms, new Phones(), new AuthSettings(Key, TimeSpan.FromHours(12), dev), _clock);

    private async Task<string> Code(string phone = "0821234567") => (await Handlers().Handle(new RequestOtp(new(phone)), default)).Value!.DevCode!;

    [Fact]
    public async Task A_code_is_sent_by_sms_in_the_persons_language_and_signs_them_in_once()
    {
        var code = await Code("+27 82 123 4567");

        var sms = Assert.Single(_sms.Sent);
        Assert.Equal(("otp.code", "zu", "sms", "+27821234567"), (sms.TemplateCode, sms.Language, sms.Channel, sms.Destination));
        Assert.Equal(code, sms.Values["code"]);
        var session = (await Handlers().Handle(new VerifyOtp(new("0821234567", code)), default)).Value!;
        Assert.Equal((_thandi.Id, "Thandi"), (session.PersonId, session.DisplayName));
        Assert.Equal(_thandi.Id.ToString(), ExpiringToken.Verify(Key, session.AccessToken, _clock.GetUtcNow()));
        Assert.Equal(_clock.GetUtcNow().AddHours(12), session.ExpiresAt);
        Assert.Equal("otp.invalid", (await Handlers().Handle(new VerifyOtp(new("0821234567", code)), default)).Error!.Code);   // used up
    }

    [Fact]
    public async Task The_code_is_only_shown_in_development_and_an_unknown_number_gets_the_same_answer_with_nothing_sent()
    {
        var known = (await Handlers(dev: false).Handle(new RequestOtp(new("0821234567")), default)).Value!;
        var unknown = (await Handlers().Handle(new RequestOtp(new("0839999999")), default)).Value!;

        Assert.Null(known.DevCode);
        Assert.Equal(known.Message, unknown.Message);
        Assert.Null(unknown.DevCode);
        Assert.Single(_sms.Sent);
    }

    [Fact]
    public async Task A_wrong_code_five_times_uses_up_the_challenge_even_for_the_right_code()
    {
        var code = await Code();
        for (var i = 0; i < 5; i++) Assert.Equal("otp.invalid", (await Handlers().Handle(new VerifyOtp(new("0821234567", "000000" == code ? "111111" : "000000")), default)).Error!.Code);

        Assert.Equal("otp.invalid", (await Handlers().Handle(new VerifyOtp(new("0821234567", code)), default)).Error!.Code);
    }

    [Fact]
    public async Task A_code_expires_after_ten_minutes_and_a_new_one_replaces_the_old()
    {
        var first = await Code();
        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal("otp.invalid", (await Handlers().Handle(new VerifyOtp(new("0821234567", first)), default)).Error!.Code);

        var second = await Code();
        Assert.True((await Handlers().Handle(new VerifyOtp(new("0821234567", second)), default)).IsSuccess);
    }

    [Fact]
    public async Task Asking_for_codes_is_limited_to_five_an_hour_and_a_bad_number_is_refused()
    {
        for (var i = 0; i < 5; i++) await Code();

        Assert.Equal("otp.too_many", (await Handlers().Handle(new RequestOtp(new("0821234567")), default)).Error!.Code);
        _clock.Advance(TimeSpan.FromHours(1.1));
        Assert.True((await Handlers().Handle(new RequestOtp(new("0821234567")), default)).IsSuccess);
        Assert.Equal("otp.phone_invalid", (await Handlers().Handle(new RequestOtp(new("12")), default)).Error!.Code);
    }

    [Fact]
    public async Task Two_people_on_one_number_means_nobody_signs_in_with_it()
    {
        _store.People.Add(new(Guid.NewGuid(), "Sipho", "0821234567", "en"));

        var result = (await Handlers().Handle(new RequestOtp(new("0821234567")), default)).Value!;

        Assert.Null(result.DevCode);
        Assert.Empty(_sms.Sent);
    }
}
