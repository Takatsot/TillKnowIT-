using Identity.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Staff;

/// <summary>Change a PIN and/or disable or re-enable a staff member. Null means "leave as is".</summary>
public sealed record UpdateStaff(Guid MembershipId, string? Pin, bool? Disabled) : ICommand<Unit>;

internal sealed class UpdateStaffHandler(IStaffStore store, IPinHasher hasher) : ICommandHandler<UpdateStaff, Unit>
{
    public async Task<Result<Unit>> Handle(UpdateStaff command, CancellationToken ct)
    {
        if (command.Pin is not null && !StaffRules.IsValidPin(command.Pin))
            return Result<Unit>.Fail("staff.pin_invalid", "PIN must be 4 to 6 digits.");

        var membership = await store.GetMembership(command.MembershipId, ct);
        if (membership is null)
            return Result<Unit>.Fail("staff.not_found", "Staff member not found.");

        if (command.Pin is not null) membership.SetPinHash(hasher.Hash(command.Pin));
        if (command.Disabled is true) membership.Disable();
        else if (command.Disabled is false) membership.Enable();

        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}
