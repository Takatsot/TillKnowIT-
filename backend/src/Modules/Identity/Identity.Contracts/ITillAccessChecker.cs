using TillKnowIT.SharedKernel;

namespace Identity.Contracts;

/// <summary>
/// Lets other modules ask whether a till and a staff member may do something in a shop,
/// without reading Identity's tables. Failure codes: device.unknown, device.not_approved,
/// staff.unknown, staff.disabled, permission.denied.
/// </summary>
public interface ITillAccessChecker
{
    Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct);
}
