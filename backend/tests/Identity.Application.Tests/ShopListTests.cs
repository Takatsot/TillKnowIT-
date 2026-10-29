using Identity.Application.Shops;
using Identity.Contracts;

namespace Identity.Application.Tests;

public class ShopListTests
{
    private sealed class Store : IShopListStore
    {
        public Task<List<StaffListItem>> Staff(Guid shopId, CancellationToken ct) => Task.FromResult(new List<StaffListItem> { new(Guid.NewGuid(), "thandi", "0821", "cashier", false), new(Guid.NewGuid(), "Anna", "0822", "owner", false) });
        public Task<List<DeviceListItem>> Devices(Guid shopId, CancellationToken ct) => Task.FromResult(new List<DeviceListItem> { new(Guid.NewGuid(), "Till 2", "pending", null, false), new(Guid.NewGuid(), "Till 1", "approved", DateTimeOffset.UtcNow, false) });
    }

    private sealed class Access(params string[] granted) : IOrganisationAccess
    {
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(granted.Contains(perm));
    }

    [Fact]
    public async Task The_owner_sees_staff_and_tills_sorted_by_name()
    {
        var handlers = new ShopListHandlers(new Store(), new Access("staff.manage", "device.manage"));

        var staff = await handlers.Handle(new ListShopStaff(Guid.NewGuid(), Guid.NewGuid()), default);
        var devices = await handlers.Handle(new ListShopDevices(Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.Equal(["Anna", "thandi"], staff.Value!.Select(s => s.Name));
        Assert.Equal(["Till 1", "Till 2"], devices.Value!.Select(d => d.Name));
    }

    [Fact]
    public async Task Someone_without_the_permission_sees_neither()
    {
        var handlers = new ShopListHandlers(new Store(), new Access());

        Assert.Equal("permission.denied", (await handlers.Handle(new ListShopStaff(Guid.NewGuid(), Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("permission.denied", (await handlers.Handle(new ListShopDevices(Guid.NewGuid(), Guid.NewGuid()), default)).Error!.Code);
    }
}
