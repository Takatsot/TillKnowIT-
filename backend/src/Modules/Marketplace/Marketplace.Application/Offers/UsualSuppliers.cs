using Identity.Contracts;
using Marketplace.Application.Suppliers;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Offers;

public sealed record UsualSupplierResponse(Guid SupplierId, string Name, DateTimeOffset? LastOrderAt);

/// <summary>The suppliers a shop buys from (set by hand, or made usual by receiving an order), so the screen can show and change them.</summary>
public sealed record ListUsualSuppliers(Guid ShopId, Guid CallerPersonId) : IQuery<List<UsualSupplierResponse>>;

internal sealed class ListUsualSuppliersHandler(IOfferStore offers, ISupplierStore suppliers, IOrganisationAccess access) : IQueryHandler<ListUsualSuppliers, List<UsualSupplierResponse>>
{
    public async Task<Result<List<UsualSupplierResponse>>> Handle(ListUsualSuppliers q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.ShopId, "stock.receive", ct)) return Result<List<UsualSupplierResponse>>.Fail("permission.denied", "You do not have access to this shop.");
        var list = new List<UsualSupplierResponse>();
        foreach (var s in await offers.ShopSuppliers(q.ShopId, ct))
            if (await suppliers.Get(s.SupplierId, ct) is { } supplier) list.Add(new(supplier.Id, supplier.Name, s.LastOrderAt));
        return Result<List<UsualSupplierResponse>>.Ok(list.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }
}
