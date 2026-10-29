using TillKnowIT.SharedKernel;

namespace Identity.Contracts;

/// <summary>A staff member as an approved till downloads them, so the till can check PINs with no connection. PinHash is the PBKDF2 hash, never the PIN.</summary>
public sealed record DeviceStaffResponse(Guid StaffId, string Name, string Role, string? PinHash, bool Disabled);

/// <summary>Whether a till is an approved device of the shop. Other modules use this before they hand a till shop data to keep offline.</summary>
public interface IDeviceGate
{
    Task<Result> Approved(Guid shopId, Guid deviceId, CancellationToken ct);
}
