using System.Security.Cryptography;
using System.Text;
using TillKnowIT.SharedKernel;

namespace Marketplace.Domain;

public enum BuyingGroupKind { Stokvel, School, Other }

/// <summary>
/// G6-1 and M6-4: a stokvel, school group or other club of shops that buys together. It is invite-only: a shop joins with a code the group admin shares.
/// A group deal can be limited to a group, so only its members see it and commit to it.
/// </summary>
public sealed class BuyingGroup : VersionedEntity
{
    public const int MaxMembers = 500;
    public static readonly TimeSpan InviteLife = TimeSpan.FromDays(7);

    private BuyingGroup() { }

    public string Name { get; private set; } = default!;
    public BuyingGroupKind Kind { get; private set; }
    public Guid AdminShopId { get; private set; }
    public Guid AdminPersonId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Result<BuyingGroup> Create(string? name, string? kind, Guid adminShopId, Guid adminPersonId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return Result<BuyingGroup>.Fail("buying_group.name_invalid", "Give the group a name of up to 100 letters.");
        if (!Enum.TryParse<BuyingGroupKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)) return Result<BuyingGroup>.Fail("buying_group.kind_invalid", "A group is a stokvel, a school or other.");
        var g = new BuyingGroup { Name = name.Trim(), Kind = parsed, AdminShopId = adminShopId, AdminPersonId = adminPersonId };
        g.StampFromDevice(Guid.CreateVersion7(), now, adminPersonId, null);
        return Result<BuyingGroup>.Ok(g);
    }

    public void Close()
    {
        IsActive = false;
        Touch();
    }
}

public enum MemberStatus { Joined, Left, Removed }

/// <summary>A shop in a buying group. The admin shop is a member from the start.</summary>
public sealed class BuyingGroupMember : VersionedEntity
{
    private BuyingGroupMember() { }

    public Guid GroupId { get; private set; }
    public Guid ShopId { get; private set; }
    public MemberStatus Status { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }

    public bool IsMember => Status == MemberStatus.Joined;

    public static BuyingGroupMember Join(Guid groupId, Guid shopId, Guid personId, DateTimeOffset now)
    {
        var m = new BuyingGroupMember { GroupId = groupId, ShopId = shopId, Status = MemberStatus.Joined, JoinedAt = now };
        m.StampFromDevice(Guid.CreateVersion7(), now, personId, null);
        return m;
    }

    public void Rejoin(DateTimeOffset now)
    {
        Status = MemberStatus.Joined;
        JoinedAt = now;
        Touch();
    }

    public void Leave(bool removed)
    {
        Status = removed ? MemberStatus.Removed : MemberStatus.Left;
        Touch();
    }
}

/// <summary>One single-use code that lets one shop join. Only its hash is kept, so the code cannot be read back from the database.</summary>
public sealed class BuyingGroupInvite : Entity
{
    private BuyingGroupInvite() { }

    public Guid GroupId { get; private set; }
    public string CodeHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? UsedByShopId { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O, 1/I: easy to read out over a phone

    public static (BuyingGroupInvite Invite, string Code) Create(Guid groupId, Guid personId, DateTimeOffset now)
    {
        var code = new string(Enumerable.Range(0, 8).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
        var invite = new BuyingGroupInvite { GroupId = groupId, CodeHash = Hash(code), ExpiresAt = now + BuyingGroup.InviteLife };
        invite.StampFromDevice(Guid.CreateVersion7(), now, personId, null);
        return (invite, code);
    }

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "")))).ToLowerInvariant();

    public Result Use(Guid shopId, DateTimeOffset now)
    {
        if (UsedAt is not null) return Result.Fail("invite.used", "This code was already used.");
        if (now >= ExpiresAt) return Result.Fail("invite.expired", "This code has expired. Ask the group admin for a new one.");
        UsedByShopId = shopId;
        UsedAt = now;
        return Result.Ok();
    }
}
