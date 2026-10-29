using Identity.Contracts;
using Identity.Domain;
using TillKnowIT.Security;
using Identity.Application.Auth;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Devices;

public interface IDeviceStore
{
    Task<Organisation?> GetShop(Guid shopId, CancellationToken ct);
    Task<Device?> GetDevice(Guid deviceId, CancellationToken ct);
    Task Add(Device device, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public sealed record RegisterDevice(Guid ShopId, string Name, Guid CallerPersonId) : ICommand<RegisterDeviceResponse>;

public sealed record ApproveDevice(Guid DeviceId, Guid CallerPersonId) : ICommand<Unit>;

public sealed record WipeDevice(Guid DeviceId, Guid CallerPersonId) : ICommand<Unit>;

/// <summary>For a till that lost its credential: gives it a new setup code and takes the old credential away until the owner approves it again.</summary>
public sealed record ResetDeviceCredential(Guid DeviceId, Guid CallerPersonId) : ICommand<RegisterDeviceResponse>;

/// <summary>The till calls this at start-up and on every sync. A wipe request means: clear local data now.</summary>
public sealed record CheckInDevice(Guid DeviceId, string? EnrolmentCode = null, string? DeviceToken = null) : ICommand<DeviceStatusResponse>;

public sealed record ConfirmDeviceWiped(Guid DeviceId) : ICommand<Unit>;

internal sealed class RegisterDeviceHandler(IDeviceStore store, IOrganisationAccess access) : ICommandHandler<RegisterDevice, RegisterDeviceResponse>
{
    public async Task<Result<RegisterDeviceResponse>> Handle(RegisterDevice command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 100)
            return Result<RegisterDeviceResponse>.Fail("device.name_invalid", "Device name is required (up to 100 characters).");

        if (!await access.Has(command.CallerPersonId, command.ShopId, "device.manage", ct))
            return Result<RegisterDeviceResponse>.Fail("permission.denied", "Only the owner can add a till.");
        var shop = await store.GetShop(command.ShopId, ct);
        if (shop is null || shop.Type != OrganisationType.Shop)
            return Result<RegisterDeviceResponse>.Fail("shop.not_found", "Shop not found.");

        var device = new Device(shop.Id, command.Name.Trim());
        var code = device.NewEnrolmentCode();
        await store.Add(device, ct);
        await store.Save(ct);
        return Result<RegisterDeviceResponse>.Ok(new(device.Id, code));
    }
}

internal sealed class ApproveDeviceHandler(IDeviceStore store, IOrganisationAccess access) : ICommandHandler<ApproveDevice, Unit>
{
    public async Task<Result<Unit>> Handle(ApproveDevice command, CancellationToken ct)
    {
        var device = await store.GetDevice(command.DeviceId, ct);
        if (device is null) return Result<Unit>.Fail("device.not_found", "Device not found.");
        if (!await access.Has(command.CallerPersonId, device.ShopId, "device.manage", ct)) return Result<Unit>.Fail("permission.denied", "Only the owner can approve a till.");

        var result = device.Approve();
        if (!result.IsSuccess) return Result<Unit>.Fail(result.Error!.Code, result.Error.Message);

        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}

internal sealed class WipeDeviceHandler(IDeviceStore store, IOrganisationAccess access) : ICommandHandler<WipeDevice, Unit>
{
    public async Task<Result<Unit>> Handle(WipeDevice command, CancellationToken ct)
    {
        var device = await store.GetDevice(command.DeviceId, ct);
        if (device is null) return Result<Unit>.Fail("device.not_found", "Device not found.");
        if (!await access.Has(command.CallerPersonId, device.ShopId, "device.manage", ct)) return Result<Unit>.Fail("permission.denied", "Only the owner can switch a till off.");

        device.Wipe();
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}

internal sealed class ResetDeviceCredentialHandler(IDeviceStore store, IOrganisationAccess access) : ICommandHandler<ResetDeviceCredential, RegisterDeviceResponse>
{
    public async Task<Result<RegisterDeviceResponse>> Handle(ResetDeviceCredential command, CancellationToken ct)
    {
        var device = await store.GetDevice(command.DeviceId, ct);
        if (device is null) return Result<RegisterDeviceResponse>.Fail("device.not_found", "Device not found.");
        if (!await access.Has(command.CallerPersonId, device.ShopId, "device.manage", ct)) return Result<RegisterDeviceResponse>.Fail("permission.denied", "Only the owner can reset a till.");

        var code = device.ResetCredential();
        if (!code.IsSuccess) return Result<RegisterDeviceResponse>.Fail(code.Error!.Code, code.Error.Message);
        await store.Save(ct);
        return Result<RegisterDeviceResponse>.Ok(new(device.Id, code.Value!));
    }
}

/// <summary>
/// The till reports in. A till that has never been set up must give its one-time setup code; after that it must show the signed token it was given last time. Either way
/// it is given a fresh token. Tills that are not approved can check in (to learn they are pending) but the token opens nothing until the owner approves them.
/// </summary>
internal sealed class CheckInDeviceHandler(IDeviceStore store, TimeProvider clock, AuthSettings? settings = null)
    : ICommandHandler<CheckInDevice, DeviceStatusResponse>
{
    public static readonly TimeSpan DeviceTokenLife = TimeSpan.FromDays(30);

    public async Task<Result<DeviceStatusResponse>> Handle(CheckInDevice command, CancellationToken ct)
    {
        var device = await store.GetDevice(command.DeviceId, ct);
        if (device is null) return Result<DeviceStatusResponse>.Fail("device.not_found", "Device not found.");

        var now = clock.GetUtcNow();
        string? token = null;
        if (settings is not null)
        {
            if (device.IsEnrolled)
            {
                if (ExpiringToken.Verify(settings.TokenKey, command.DeviceToken, now) != device.TokenSubject)
                    return Result<DeviceStatusResponse>.Fail("device.credential_required", "This till must sign in with its saved credential. If it was lost, add the till again.");
            }
            else
            {
                var enrolled = device.Enrol(command.EnrolmentCode, now);
                if (!enrolled.IsSuccess) return Result<DeviceStatusResponse>.Fail(enrolled.Error!.Code, enrolled.Error.Message);
            }
            token = ExpiringToken.Issue(settings.TokenKey, device.TokenSubject, now, DeviceTokenLife).Token;
        }

        device.RecordSeen(now);
        await store.Save(ct);
        return Result<DeviceStatusResponse>.Ok(new DeviceStatusResponse(device.Id, device.Status.ToString().ToLowerInvariant(), device.WipeRequested, token));
    }
}

internal sealed class ConfirmDeviceWipedHandler(IDeviceStore store) : ICommandHandler<ConfirmDeviceWiped, Unit>
{
    public async Task<Result<Unit>> Handle(ConfirmDeviceWiped command, CancellationToken ct)
    {
        var device = await store.GetDevice(command.DeviceId, ct);
        if (device is null) return Result<Unit>.Fail("device.not_found", "Device not found.");

        device.ConfirmWiped();
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}
