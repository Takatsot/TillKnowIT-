using TillKnowIT.SharedKernel;

namespace Identity.Domain;

public enum MembershipStatus { Active, Disabled }

public sealed class Membership : VersionedEntity
{
    public const int MaxPinAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private Membership() { }

    public Membership(Guid personId, Guid organisationId, Guid roleId)
    {
        PersonId = personId;
        OrganisationId = organisationId;
        RoleId = roleId;
    }

    public Guid PersonId { get; private set; }
    public Guid OrganisationId { get; private set; }
    public Guid RoleId { get; private set; }
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;
    public string? PinHash { get; private set; }
    public int FailedPinAttempts { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    public bool IsLocked(DateTimeOffset now) => LockedUntil > now;

    public void SetPinHash(string hash)
    {
        PinHash = hash;
        FailedPinAttempts = 0;
        LockedUntil = null;
        Touch();
    }

    /// <summary>5 wrong tries lock the PIN for 5 minutes.</summary>
    public void RecordPinFailure(DateTimeOffset now)
    {
        FailedPinAttempts++;
        if (FailedPinAttempts >= MaxPinAttempts)
        {
            LockedUntil = now + LockDuration;
            FailedPinAttempts = 0;
        }
        Touch();
    }

    public void RecordPinSuccess()
    {
        if (FailedPinAttempts == 0 && LockedUntil is null) return;
        FailedPinAttempts = 0;
        LockedUntil = null;
        Touch();
    }

    public void Disable()
    {
        Status = MembershipStatus.Disabled;
        Touch();
    }

    public void Enable()
    {
        Status = MembershipStatus.Active;
        Touch();
    }
}
