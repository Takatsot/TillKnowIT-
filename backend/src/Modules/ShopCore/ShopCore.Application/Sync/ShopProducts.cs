using Identity.Contracts;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Sync;

/// <summary>Every product of the shop with its prices, for the owner's back office (a till uses the pull instead). Inactive products are included so they can be switched back on.</summary>
public sealed record ListShopProducts(Guid ShopId, Guid CallerPersonId) : IQuery<List<PulledProduct>>;

internal sealed class ListShopProductsHandler(IProductPullStore store, IOrganisationAccess access) : IQueryHandler<ListShopProducts, List<PulledProduct>>
{
    public async Task<Result<List<PulledProduct>>> Handle(ListShopProducts q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.ShopId, "product.edit", ct) && !await access.Has(q.CallerPersonId, q.ShopId, "report.view", ct))
            return Result<List<PulledProduct>>.Fail("permission.denied", "You are not allowed to see the products.");
        return Result<List<PulledProduct>>.Ok((await store.ChangedSince(q.ShopId, null, PullProductsHandler.MaxProducts, ct)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }
}
