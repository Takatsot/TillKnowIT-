using Identity.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Staff;

public sealed record AddStaff(Guid ShopId, string Name, string Phone, string RoleCode, string Pin) : ICommand<Guid>;

internal sealed class AddStaffHandler(IStaffStore store, IPinHasher hasher) : ICommandHandler<AddStaff, Guid>
{
    public async Task<Result<Guid>> Handle(AddStaff command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Result<Guid>.Fail("staff.name_required", "Staff name is required.");
        if (!StaffRules.IsValidPin(command.Pin))
            return Result<Guid>.Fail("staff.pin_invalid", "PIN must be 4 to 6 digits.");
        var phone = StaffRules.NormalisePhone(command.Phone);
        if (phone is null)
            return Result<Guid>.Fail("staff.phone_invalid", "Phone number is not valid.");
        if (!StaffRules.AssignableRoles.Contains(command.RoleCode))
            return Result<Guid>.Fail("staff.role_invalid", "Staff can be a manager or a cashier.");

        var shop = await store.GetShop(command.ShopId, ct);
        if (shop is null || shop.Type != OrganisationType.Shop)
            return Result<Guid>.Fail("shop.not_found", "Shop not found.");
        var role = await store.FindRole(command.RoleCode, ct);
        if (role is null || !role.IsActive)
            return Result<Guid>.Fail("staff.role_invalid", "Role is not available.");

        var person = await store.FindPersonByPhone(phone, ct);
        if (person is null)
        {
            person = new Person(command.Name.Trim(), phone);
            await store.Add(person, ct);
        }
        else if (await store.MembershipExists(person.Id, shop.Id, ct))
        {
            return Result<Guid>.Fail("staff.already_member", "This person already works at the shop.");
        }

        var membership = new Membership(person.Id, shop.Id, role.Id);
        membership.SetPinHash(hasher.Hash(command.Pin));
        await store.Add(membership, ct);
        await store.Save(ct);
        return Result<Guid>.Ok(membership.Id);
    }
}
