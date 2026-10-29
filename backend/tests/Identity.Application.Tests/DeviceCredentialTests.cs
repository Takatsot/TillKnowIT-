using Identity.Application.Auth;
using Identity.Application.Devices;
using Identity.Contracts;
using Identity.Domain;
using Microsoft.Extensions.Time.Testing;
using TillKnowIT.Security;

namespace Identity.Application.Tests;

public class DeviceCredentialTests
{
    private sealed class Store : IDeviceStore
    {
        public Organisation? Shop { get; set; }
        public List<Device> Devices { get; } = [];
        public Task<Organisation?> GetShop(Guid shopId, CancellationToken ct) => Task.FromResult(Shop);
        public Task<Device?> GetDevice(Guid id, CancellationToken ct) => Task.FromResult(Devices.FirstOrDefault(d => d.Id == id));
        public Task Add(Device d, CancellationToken ct) { Devices.Add(d); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private const string Key = "a-test-key-that-is-at-least-32-characters-long";
    private readonly Store _store = new() { Shop = Organisation.CreateShop("Shop", Guid.NewGuid()) };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));

    private CheckInDeviceHandler CheckIn => new(_store, _clock, new AuthSettings(Key, TimeSpan.FromHours(12), false));

    private async Task<(Guid Id, string Code)> Register()
    {
        var r = (await new RegisterDeviceHandler(_store).Handle(new RegisterDevice(Guid.NewGuid(), "Till 1"), default)).Value!;
        return (r.DeviceId, r.EnrolmentCode);
    }

    [Fact]
    public async Task A_new_till_sets_itself_up_with_its_code_once_and_gets_a_token_for_that_device()
    {
        var (id, code) = await Register();

        var first = (await CheckIn.Handle(new CheckInDevice(id, code.ToLowerInvariant()), default)).Value!;

        Assert.Equal($"device:{id}", ExpiringToken.Verify(Key, first.Token, _clock.GetUtcNow()));
        Assert.Equal("device.credential_required", (await CheckIn.Handle(new CheckInDevice(id, code), default)).Error!.Code);   // the code is used up: only the token gets in now
    }

    [Fact]
    public async Task A_wrong_or_missing_code_is_refused_and_gives_no_token()
    {
        var (id, _) = await Register();

        Assert.Equal("device.enrolment_required", (await CheckIn.Handle(new CheckInDevice(id, "WRONGCODE1"), default)).Error!.Code);
        Assert.Equal("device.enrolment_required", (await CheckIn.Handle(new CheckInDevice(id), default)).Error!.Code);
    }

    [Fact]
    public async Task After_setup_the_till_must_show_its_token_and_gets_a_fresh_one_each_time()
    {
        var (id, code) = await Register();
        var token = (await CheckIn.Handle(new CheckInDevice(id, code), default)).Value!.Token;
        _clock.Advance(TimeSpan.FromDays(1));

        var again = (await CheckIn.Handle(new CheckInDevice(id, null, token), default)).Value!;

        Assert.NotEqual(token, again.Token);
        Assert.Equal("device.credential_required", (await CheckIn.Handle(new CheckInDevice(id), default)).Error!.Code);
        Assert.Equal("device.credential_required", (await CheckIn.Handle(new CheckInDevice(id, null, "garbage"), default)).Error!.Code);
        var otherDevice = (await Register()).Id;
        Assert.Equal("device.credential_required", (await CheckIn.Handle(new CheckInDevice(id, null, ExpiringToken.Issue(Key, $"device:{otherDevice}", _clock.GetUtcNow(), TimeSpan.FromDays(1)).Token), default)).Error!.Code);   // another till's token is not this till's
    }

    [Fact]
    public async Task A_token_stops_working_after_thirty_days()
    {
        var (id, code) = await Register();
        var token = (await CheckIn.Handle(new CheckInDevice(id, code), default)).Value!.Token;
        _clock.Advance(TimeSpan.FromDays(31));

        Assert.Equal("device.credential_required", (await CheckIn.Handle(new CheckInDevice(id, null, token), default)).Error!.Code);
    }
}
