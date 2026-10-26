using Identity.Contracts;
using Identity.Domain;
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

public sealed record RegisterDevice(Guid ShopId, string Name) : ICommand<Guid>;

public sealed record ApproveDevice(Guid DeviceId) : ICommand<Unit>;

public sealed record WipeDevice(Guid DeviceId) : ICommand<Unit>;

/// <summary>The till calls this at start-up and on every sync. A wipe request means: clear local data now.</summary>
public sealed record CheckInDevice(Guid DeviceId) : ICommand<DeviceStatusResponse>;

public sealed record ConfirmDeviceWiped(Guid DeviceId) : ICommand<Unit>;

internal sealed class RegisterDeviceHandler(IDeviceStore store) : ICommandHandler<RegisterDevice, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterDevice command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 100)
            return Result<Guid>.Fail("device.name_invalid", "Device name is required (up to 100 characters).");

        var shop = await store.GetShop(command.ShopId, ct);
        if (shop is null || shop.Type != OrganisationType.Shop)
            return Result<Guid>.Fail("shop.not_found", "Shop not found.");

        var device = new Device(shop.Id, command.Name.Trim());
        await store.Add(device, ct);
        await store.Save(ct);
        return Result<Guid>.Ok(device.Id);
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

internal sealed class CheckInDeviceHandler(IDeviceStore store, TimeProvider clock)
    : ICommandHandler<CheckInDevice, DeviceStatusResponse>
{
    public async Task<Result<DeviceStatusResponse>> Handle(CheckInDevice command, CancellationToken ct)
    {
        var device = await store.GetDevice(command.DeviceId, ct);
        if (device is null) return Result<DeviceStatusResponse>.Fail("device.not_found", "Device not found.");

        device.RecordSeen(clock.GetUtcNow());
        await store.Save(ct);
        return Result<DeviceStatusResponse>.Ok(new DeviceStatusResponse(device.Id, device.Status.ToString().ToLowerInvariant(), device.WipeRequested));
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
