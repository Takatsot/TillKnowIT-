using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Devices;

public interface IDeviceStaffStore
{
    Task<List<DeviceStaffResponse>> StaffOf(Guid shopId, CancellationToken ct);
}

/// <summary>The staff list for a till to keep. Only an approved device of that shop gets it.</summary>
public sealed record GetDeviceStaff(Guid ShopId, Guid DeviceId) : IQuery<List<DeviceStaffResponse>>;

internal sealed class GetDeviceStaffHandler(IDeviceGate gate, IDeviceStaffStore store) : IQueryHandler<GetDeviceStaff, List<DeviceStaffResponse>>
{
    public async Task<Result<List<DeviceStaffResponse>>> Handle(GetDeviceStaff q, CancellationToken ct)
    {
        var allowed = await gate.Approved(q.ShopId, q.DeviceId, ct);
        if (!allowed.IsSuccess) return Result<List<DeviceStaffResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<List<DeviceStaffResponse>>.Ok((await store.StaffOf(q.ShopId, ct)).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }
}
