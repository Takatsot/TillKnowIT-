using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TillKnowIT.SharedKernel;

namespace Identity.Infrastructure;

internal sealed class TillAccessChecker(IdentityDbContext db) : ITillAccessChecker
{
    public async Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct)
    {
        var device = await db.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId && d.ShopId == shopId)
            .Select(d => new { d.Status })
            .FirstOrDefaultAsync(ct);
        if (device is null) return Result.Fail("device.unknown", "This device is not registered to the shop.");
        if (device.Status != DeviceStatus.Approved) return Result.Fail("device.not_approved", "This device is not approved or was switched off.");

        // staffId is the membership id, the same id the till unlocks with.
        var membership = await db.Memberships.AsNoTracking()
            .Where(m => m.Id == staffId && m.OrganisationId == shopId)
            .Select(m => new
            {
                m.Status,
                Allowed = db.Roles.Any(r => r.Id == m.RoleId && r.Permissions.Any(p => p.PermissionCode == permission)),
            })
            .FirstOrDefaultAsync(ct);
        if (membership is null) return Result.Fail("staff.unknown", "This staff member does not work at the shop.");
        if (membership.Status != MembershipStatus.Active) return Result.Fail("staff.disabled", "This staff member is disabled.");
        if (!membership.Allowed) return Result.Fail("permission.denied", $"This role may not do '{permission}'.");

        return Result.Ok();
    }
}
