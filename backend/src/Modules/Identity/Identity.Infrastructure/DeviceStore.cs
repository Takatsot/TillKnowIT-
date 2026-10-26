using Identity.Application.Devices;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure;

internal sealed class DeviceStore(IdentityDbContext db) : IDeviceStore
{
    public Task<Organisation?> GetShop(Guid shopId, CancellationToken ct) =>
        db.Organisations.FirstOrDefaultAsync(o => o.Id == shopId, ct);

    public Task<Device?> GetDevice(Guid deviceId, CancellationToken ct) =>
        db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);

    public async Task Add(Device device, CancellationToken ct) => await db.Devices.AddAsync(device, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
