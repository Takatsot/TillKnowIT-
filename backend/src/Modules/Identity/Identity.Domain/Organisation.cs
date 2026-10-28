using Identity.Contracts;
using TillKnowIT.SharedKernel;

namespace Identity.Domain;

public enum OrganisationType { Platform, Shop, Supplier }

public enum OrganisationStatus { Pending, Verified, Suspended }

public sealed class Organisation : VersionedEntity
{
    private Organisation() { }

    public static Organisation CreateShop(string name, Guid ownerPersonId, string? municipalityCode = null)
    {
        var shop = new Organisation { Type = OrganisationType.Shop, Name = name, Status = OrganisationStatus.Pending, MunicipalityCode = municipalityCode };
        shop.Raise(new ShopCreated(shop.Id, ownerPersonId, name));
        return shop;
    }

    /// <summary>A supplier's business account. It stays pending until the platform admin verifies the supplier.</summary>
    public static Organisation CreateSupplier(string name) =>
        new() { Type = OrganisationType.Supplier, Name = name.Trim(), Status = OrganisationStatus.Pending };

    /// <summary>G1-1: a branch is a shop that belongs to a head shop (one level only). The head shop's owner sees every branch; each branch has its own tills, stock and sales.</summary>
    public static Result<Organisation> CreateBranch(Organisation head, string? name, string? municipalityCode)
    {
        if (head.Type != OrganisationType.Shop) return Result<Organisation>.Fail("branch.head_invalid", "Only a shop can have branches.");
        if (head.ParentId is not null) return Result<Organisation>.Fail("branch.nested", "A branch cannot have branches of its own.");
        if (string.IsNullOrWhiteSpace(name)) return Result<Organisation>.Fail("shop.name_required", "Shop name is required.");
        return Result<Organisation>.Ok(new Organisation { Type = OrganisationType.Shop, Name = name.Trim(), Status = OrganisationStatus.Pending, MunicipalityCode = municipalityCode ?? head.MunicipalityCode, ParentId = head.Id });
    }

    public Guid? ParentId { get; private set; }

    public OrganisationType Type { get; private set; }

    /// <summary>Where the shop trades, from the municipalities lookup. Suppliers deliver by municipality, so offers need it.</summary>
    public string? MunicipalityCode { get; private set; }

    public void MoveTo(string municipalityCode)
    {
        MunicipalityCode = municipalityCode;
        Touch();
    }
    public string Name { get; private set; } = default!;
    public OrganisationStatus Status { get; private set; }

    public void Suspend()
    {
        Status = OrganisationStatus.Suspended;
        Touch();
    }

    /// <summary>Back to pending, so a field agent has to verify the shop again.</summary>
    public void Reinstate()
    {
        if (Status != OrganisationStatus.Suspended) return;
        Status = OrganisationStatus.Pending;
        Touch();
    }

    /// <summary>Field agent approval. A suspended shop cannot be verified; it has to be reinstated first.</summary>
    public Result Verify()
    {
        if (Status == OrganisationStatus.Suspended)
            return Result.Fail("shop.suspended", "This shop is suspended.");
        if (Status == OrganisationStatus.Verified) return Result.Ok();
        Status = OrganisationStatus.Verified;
        Touch();
        return Result.Ok();
    }
}
