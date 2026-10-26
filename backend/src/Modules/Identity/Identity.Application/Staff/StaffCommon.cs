using System.Text.RegularExpressions;
using Identity.Domain;

namespace Identity.Application.Staff;

public interface IPinHasher
{
    string Hash(string pin);
    bool Verify(string pin, string hash);
}

public interface IStaffStore
{
    Task<Organisation?> GetShop(Guid shopId, CancellationToken ct);
    Task<Role?> FindRole(string code, CancellationToken ct);
    Task<Person?> FindPersonByPhone(string phone, CancellationToken ct);
    Task<Membership?> GetMembership(Guid membershipId, CancellationToken ct);
    Task<bool> MembershipExists(Guid personId, Guid organisationId, CancellationToken ct);
    Task Add(Person person, CancellationToken ct);
    Task Add(Membership membership, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public static partial class StaffRules
{
    /// <summary>Roles an owner may hand out at a till. Owner, field agent and platform roles are never assigned this way.</summary>
    public static readonly string[] AssignableRoles = ["manager", "cashier"];

    public static bool IsValidPin(string? pin) => pin is not null && PinPattern().IsMatch(pin);

    /// <summary>Returns +27… for local numbers, or null if the number is not usable.</summary>
    public static string? NormalisePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = "+" + digits[2..];
        if (digits.StartsWith('0')) digits = "+27" + digits[1..];
        else if (!digits.StartsWith('+')) digits = "+" + digits;
        return PhonePattern().IsMatch(digits) ? digits : null;
    }

    [GeneratedRegex(@"^\d{4,6}$")]
    private static partial Regex PinPattern();

    [GeneratedRegex(@"^\+\d{9,15}$")]
    private static partial Regex PhonePattern();
}
