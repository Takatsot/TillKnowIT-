using Identity.Contracts;
using TillKnowIT.SharedKernel;

namespace Identity.Domain;

public enum OrganisationType { Platform, Shop, Supplier }

public enum OrganisationStatus { Pending, Verified, Suspended }

public sealed class Organisation : VersionedEntity
{
    private Organisation() { }

    public static Organisation CreateShop(string name, Guid ownerPersonId)
    {
        var shop = new Organisation { Type = OrganisationType.Shop, Name = name, Status = OrganisationStatus.Pending };
        shop.Raise(new ShopCreated(shop.Id, ownerPersonId, name));
        return shop;
    }

    public OrganisationType Type { get; private set; }
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
