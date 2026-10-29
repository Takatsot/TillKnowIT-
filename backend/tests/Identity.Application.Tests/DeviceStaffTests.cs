using Identity.Application.Devices;
using Identity.Contracts;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Tests;

public class DeviceStaffTests
{
    private sealed class Gate(Result answer) : IDeviceGate
    {
        public Task<Result> Approved(Guid shopId, Guid deviceId, CancellationToken ct) => Task.FromResult(answer);
    }

    private sealed class Store : IDeviceStaffStore
    {
        public Task<List<DeviceStaffResponse>> StaffOf(Guid shopId, CancellationToken ct) => Task.FromResult(new List<DeviceStaffResponse>
        {
            new(Guid.NewGuid(), "sipho", "manager", "hash2", false),
            new(Guid.NewGuid(), "Thandi", "cashier", "hash1", false),
        });
    }

    [Fact]
    public async Task An_approved_till_gets_the_staff_with_their_pin_hashes_sorted_by_name()
    {
        var result = await new GetDeviceStaffHandler(new Gate(Result.Ok()), new Store()).Handle(new GetDeviceStaff(Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.Equal(["sipho", "Thandi"], result.Value!.Select(s => s.Name));
        Assert.All(result.Value!, s => Assert.StartsWith("hash", s.PinHash));
    }

    [Fact]
    public async Task A_till_that_is_not_approved_gets_no_staff_and_no_hashes()
    {
        var result = await new GetDeviceStaffHandler(new Gate(Result.Fail("device.not_approved", "No.")), new Store()).Handle(new GetDeviceStaff(Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.Equal("device.not_approved", result.Error!.Code);
    }
}
