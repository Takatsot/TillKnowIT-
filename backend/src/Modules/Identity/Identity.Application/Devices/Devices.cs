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

public sealed record RegisterDevice(Guid ShopId, string Name) : ICommand<RegisterDeviceResponse>;

public sealed record ApproveDevice(Guid DeviceId) : ICommand<Unit>;

public sealed record WipeDevice(Guid DeviceId) : ICommand<Unit>;

/// <summary>The till calls this at start-up and on every sync. A wipe request means: clear local data now.</summary>
public sealed record CheckInDevice(Guid DeviceId, string? EnrolmentCode = null, string? DeviceToken = null) : ICommand<DeviceStatusResponse>;

public sealed record ConfirmDeviceWiped(Guid DeviceId) : ICommand<Unit>;

internal sealed class RegisterDeviceHandler(IDeviceStore store) : ICommandHandler<RegisterDevice, RegisterDeviceResponse>
{
    public async Task<Result<RegisterDeviceResponse>> Handle(RegisterDevice command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 100)
            return Result<RegisterDeviceResponse>.Fail("device.name_invalid", "Device name is required (up to 100 characters).");

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

internal sealed class ApproveDeviceHandler(IDeviceStore store) : ICommandHandler<ApproveDevice, Unit>
{
    public async Task<Result<Unit>> Handle(ApproveDevice command, CancellationToken ct)
    {
        var device = await store.GetDevice(command.DeviceId, ct);
        if (device is null) return Result<Unit>.Fail("device.not_found", "Device not found.");

        var result = device.Approve();
        if (!result.IsSuccess) return Result<Unit>.Fail(result.Error!.Code, result.Error.Message);

        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}

internal sealed class WipeDeviceHandler(IDeviceStore store) : ICommandHandler<WipeDevice, Unit>
{
    public async Task<Result<Unit>> Handle(WipeDevice command, CancellationToken ct)
    {
        var device = await store.GetDevice(command.DeviceId, ct);
        if (device is null) return Result<Unit>.Fail("device.not_found", "Device not found.");

        device.Wipe();
        await store.Save(ct);
        return Result<Unit>.Ok(default);
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
                if (ExpiringToken.Verify(settings.TokenKey, command.DeviceToken, now) != $"device:{device.Id}")
                    return Result<DeviceStatusResponse>.Fail("device.credential_required", "This till must sign in with its saved credential. If it was lost, add the till again.");
            }
            else
            {
                var enrolled = device.Enrol(command.EnrolmentCode, now);
                if (!enrolled.IsSuccess) return Result<DeviceStatusResponse>.Fail(enrolled.Error!.Code, enrolled.Error.Message);
            }
            token = ExpiringToken.Issue(settings.TokenKey, $"device:{device.Id}", now, DeviceTokenLife).Token;
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
