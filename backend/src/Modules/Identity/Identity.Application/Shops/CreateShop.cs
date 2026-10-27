using Identity.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Shops;

public sealed record CreateShop(Guid OwnerPersonId, string Name, string Area, string Language) : ICommand<Guid>;

public interface IShopStore
{
    Task Add(Organisation shop, CancellationToken ct);
    Task<bool> PersonExists(Guid personId, CancellationToken ct);
    Task Save(CancellationToken ct);
}

internal sealed class CreateShopHandler(IShopStore store, Catalogue.Contracts.ILookupChecker lookups) : ICommandHandler<CreateShop, Guid>
{
    public async Task<Result<Guid>> Handle(CreateShop command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Result<Guid>.Fail("shop.name_required", "Shop name is required.");
        if (!await store.PersonExists(command.OwnerPersonId, ct))
            return Result<Guid>.Fail("person.not_found", "Owner not found.");

        string? municipality = null;
        if (!string.IsNullOrWhiteSpace(command.Area))
        {
            municipality = command.Area.Trim();
            if (!await lookups.IsActive("municipalities", municipality, ct))
                return Result<Guid>.Fail("shop.area_invalid", "That area is not one we know. Choose your municipality from the list.");
        }

        // Status starts as pending until a field agent verifies the shop.
        var shop = Organisation.CreateShop(command.Name.Trim(), command.OwnerPersonId, municipality);
        await store.Add(shop, ct);
        await store.Save(ct);
        return Result<Guid>.Ok(shop.Id);
    }
}
