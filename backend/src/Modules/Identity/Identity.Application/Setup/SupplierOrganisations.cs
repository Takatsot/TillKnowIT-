using Identity.Application.Shops;
using Identity.Application.Staff;
using Identity.Contracts;
using Identity.Domain;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Setup;

public sealed class SupplierOrganisationService(IShopStore organisations, IStaffStore staff) : ISupplierOrganisations
{
    public async Task<Result<Guid>> Create(string name, Guid adminPersonId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            return Result<Guid>.Fail("supplier.name_invalid", "A supplier needs a name (up to 200 characters).");
        if (!await organisations.PersonExists(adminPersonId, ct))
            return Result<Guid>.Fail("person.not_found", "That person was not found.");
        var role = await staff.FindRole("supplier_admin", ct);
        if (role is null) return Result<Guid>.Fail("role.not_found", "The supplier admin role is missing.");

        var organisation = Organisation.CreateSupplier(name);
        await organisations.Add(organisation, ct);
        await staff.Add(new Membership(adminPersonId, organisation.Id, role.Id), ct);
        await staff.Save(ct);
        return Result<Guid>.Ok(organisation.Id);
    }

    public async Task<Result<Guid>> AddRep(Guid organisationId, string name, string phone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result<Guid>.Fail("staff.name_required", "Staff name is required.");
        var normalised = StaffRules.NormalisePhone(phone);
        if (normalised is null) return Result<Guid>.Fail("staff.phone_invalid", "Phone number is not valid.");

        var organisation = await staff.GetShop(organisationId, ct);   // finds any organisation by id
        if (organisation is null || organisation.Type != OrganisationType.Supplier)
            return Result<Guid>.Fail("supplier.not_found", "Supplier not found.");
        var role = await staff.FindRole("supplier_rep", ct);
        if (role is null) return Result<Guid>.Fail("role.not_found", "The supplier rep role is missing.");

        var person = await staff.FindPersonByPhone(normalised, ct);
        if (person is null)
        {
            person = new Person(name.Trim(), normalised);
            await staff.Add(person, ct);
        }
        else if (await staff.MembershipExists(person.Id, organisationId, ct))
        {
            return Result<Guid>.Fail("staff.already_member", "This person already works at the supplier.");
        }

        var membership = new Membership(person.Id, organisationId, role.Id);
        await staff.Add(membership, ct);
        await staff.Save(ct);
        return Result<Guid>.Ok(membership.Id);
    }

    public async Task<Result> SetStatus(Guid organisationId, string status, CancellationToken ct)
    {
        var organisation = await staff.GetShop(organisationId, ct);
        if (organisation is null || organisation.Type != OrganisationType.Supplier)
            return Result.Fail("supplier.not_found", "Supplier not found.");

        switch (status)
        {
            case "verified":
                var verified = organisation.Verify();
                if (!verified.IsSuccess) return verified;
                break;
            case "suspended": organisation.Suspend(); break;
            case "pending": organisation.Reinstate(); break;
            default: return Result.Fail("supplier.status_invalid", "Status must be verified, suspended or pending.");
        }
        await staff.Save(ct);
        return Result.Ok();
    }
}
