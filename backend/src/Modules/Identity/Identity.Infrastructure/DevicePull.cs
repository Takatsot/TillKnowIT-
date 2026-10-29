using Identity.Application.Devices;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TillKnowIT.SharedKernel;

namespace Identity.Infrastructure;

internal sealed class DeviceGate(IdentityDbContext db) : IDeviceGate
{
    public async Task<Result> Approved(Guid shopId, Guid deviceId, CancellationToken ct)
    {
        var status = await db.Devices.AsNoTracking().Where(d => d.Id == deviceId && d.ShopId == shopId).Select(d => (DeviceStatus?)d.Status).FirstOrDefaultAsync(ct);
        if (status is null) return Result.Fail("device.unknown", "This device is not registered to the shop.");
        return status == DeviceStatus.Approved ? Result.Ok() : Result.Fail("device.not_approved", "This device is not approved or was switched off.");
    }
}

internal sealed class DeviceStaffStore(IdentityDbContext db) : IDeviceStaffStore
{
    public async Task<List<DeviceStaffResponse>> StaffOf(Guid shopId, CancellationToken ct) =>
        await (from m in db.Memberships.AsNoTracking()
               join p in db.People.AsNoTracking() on m.PersonId equals p.Id
               join r in db.Roles.AsNoTracking() on m.RoleId equals r.Id
               where m.OrganisationId == shopId && (r.Code == "owner" || r.Code == "manager" || r.Code == "cashier")
               select new DeviceStaffResponse(m.Id, p.DisplayName, r.Code, m.PinHash, m.Status != MembershipStatus.Active)).ToListAsync(ct);
}
