using Identity.Application.Devices;
using Identity.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Identity.Application.Tests;

public class DeviceTests
{
    private sealed class Allow(bool yes = true) : Identity.Contracts.IOrganisationAccess
    {
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(yes && perm == "device.manage");
    }
    private static readonly Allow Owner = new();

    private sealed class FakeDeviceStore : IDeviceStore
    {
        public Organisation? Shop { get; set; } = Organisation.CreateShop("Mama's Spaza", Guid.NewGuid());
        public List<Device> Devices { get; } = [];

        public Task<Organisation?> GetShop(Guid shopId, CancellationToken ct) => Task.FromResult(Shop);
        public Task<Device?> GetDevice(Guid id, CancellationToken ct) => Task.FromResult(Devices.FirstOrDefault(d => d.Id == id));
        public Task Add(Device device, CancellationToken ct) { Devices.Add(device); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private static async Task<(FakeDeviceStore Store, Guid Id)> Registered()
    {
        var store = new FakeDeviceStore();
        var result = await new RegisterDeviceHandler(store, Owner).Handle(new RegisterDevice(Guid.NewGuid(), "Counter phone", Guid.NewGuid()), default);
        return (store, result.Value!.DeviceId);
    }

    [Fact]
    public async Task New_device_is_pending_until_the_owner_approves()
    {
        var (store, id) = await Registered();
        Assert.Equal(DeviceStatus.Pending, store.Devices.Single().Status);

        var approved = await new ApproveDeviceHandler(store, Owner).Handle(new ApproveDevice(id, Guid.NewGuid()), default);

        Assert.True(approved.IsSuccess);
        Assert.Equal(DeviceStatus.Approved, store.Devices.Single().Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Device_needs_a_name(string name)
    {
        var result = await new RegisterDeviceHandler(new FakeDeviceStore(), Owner).Handle(new RegisterDevice(Guid.NewGuid(), name, Guid.NewGuid()), default);
        Assert.Equal("device.name_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Unknown_shop_is_rejected()
    {
        var store = new FakeDeviceStore { Shop = null };
        var result = await new RegisterDeviceHandler(store, Owner).Handle(new RegisterDevice(Guid.NewGuid(), "Till 1", Guid.NewGuid()), default);
        Assert.Equal("shop.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Wipe_switches_the_device_off_and_the_next_check_in_says_clear_local_data()
    {
        var (store, id) = await Registered();
        await new ApproveDeviceHandler(store, Owner).Handle(new ApproveDevice(id, Guid.NewGuid()), default);
        await new WipeDeviceHandler(store, Owner).Handle(new WipeDevice(id, Guid.NewGuid()), default);

        var checkIn = await new CheckInDeviceHandler(store, new FakeTimeProvider()).Handle(new CheckInDevice(id), default);

        Assert.Equal("disabled", checkIn.Value!.Status);
        Assert.True(checkIn.Value.WipeRequested);
    }

    [Fact]
    public async Task Wipe_request_stays_until_the_till_confirms_it_cleared_its_data()
    {
        var (store, id) = await Registered();
        await new WipeDeviceHandler(store, Owner).Handle(new WipeDevice(id, Guid.NewGuid()), default);
        var clock = new FakeTimeProvider();
        var checkIn = new CheckInDeviceHandler(store, clock);

        Assert.True((await checkIn.Handle(new CheckInDevice(id), default)).Value!.WipeRequested);
        Assert.True((await checkIn.Handle(new CheckInDevice(id), default)).Value!.WipeRequested);

        await new ConfirmDeviceWipedHandler(store).Handle(new ConfirmDeviceWiped(id), default);

        var after = (await checkIn.Handle(new CheckInDevice(id), default)).Value!;
        Assert.False(after.WipeRequested);
        Assert.Equal("disabled", after.Status);
    }

    [Fact]
    public async Task Switched_off_device_cannot_be_approved_again()
    {
        var (store, id) = await Registered();
        await new WipeDeviceHandler(store, Owner).Handle(new WipeDevice(id, Guid.NewGuid()), default);

        var result = await new ApproveDeviceHandler(store, Owner).Handle(new ApproveDevice(id, Guid.NewGuid()), default);

        Assert.Equal("device.disabled", result.Error!.Code);
        Assert.Equal(DeviceStatus.Disabled, store.Devices.Single().Status);
    }

    [Fact]
    public async Task Check_in_records_last_seen()
    {
        var (store, id) = await Registered();
        var now = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

        await new CheckInDeviceHandler(store, new FakeTimeProvider(now)).Handle(new CheckInDevice(id), default);

        Assert.Equal(now, store.Devices.Single().LastSeenAt);
    }

    [Fact]
    public async Task Unknown_device_is_not_found()
    {
        var store = new FakeDeviceStore();
        var result = await new CheckInDeviceHandler(store, new FakeTimeProvider()).Handle(new CheckInDevice(Guid.NewGuid()), default);
        Assert.Equal("device.not_found", result.Error!.Code);
    }
}
