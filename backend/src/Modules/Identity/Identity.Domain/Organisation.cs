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

    /// <summary>Field agent approval.</summary>
    public void Verify()
    {
        Status = OrganisationStatus.Verified;
        Touch();
    }
}
