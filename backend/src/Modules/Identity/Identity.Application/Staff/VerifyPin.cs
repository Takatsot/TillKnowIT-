using Identity.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Staff;

/// <summary>Server-side PIN check. The till does the same check offline against the synced hash.</summary>
public sealed record VerifyPin(Guid MembershipId, string Pin) : ICommand<Unit>;

internal sealed class VerifyPinHandler(IStaffStore store, IPinHasher hasher, TimeProvider clock) : ICommandHandler<VerifyPin, Unit>
{
    public async Task<Result<Unit>> Handle(VerifyPin command, CancellationToken ct)
    {
        var membership = await store.GetMembership(command.MembershipId, ct);
        if (membership is null || membership.Status != MembershipStatus.Active || membership.PinHash is null)
            return Result<Unit>.Fail("staff.pin_rejected", "PIN not accepted.");

        var now = clock.GetUtcNow();
        if (membership.IsLocked(now))
            return Result<Unit>.Fail("staff.locked", $"Too many wrong tries. Try again after {membership.LockedUntil:HH:mm} UTC.");

        if (!hasher.Verify(command.Pin, membership.PinHash))
        {
            membership.RecordPinFailure(now);
            await store.Save(ct);
            return membership.IsLocked(now)
                ? Result<Unit>.Fail("staff.locked", "Too many wrong tries. Locked for 5 minutes.")
                : Result<Unit>.Fail("staff.pin_rejected", "PIN not accepted.");
        }

        membership.RecordPinSuccess();
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}
