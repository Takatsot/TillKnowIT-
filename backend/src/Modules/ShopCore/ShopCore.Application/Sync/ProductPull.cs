using Identity.Contracts;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Sync;

public interface IProductPullStore
{
    Task<List<PulledProduct>> ChangedSince(Guid shopId, DateTimeOffset? since, int take, CancellationToken ct);
}

/// <summary>
/// What an approved till downloads to sell with, no connection needed afterwards. Without Since it is every product; with it, only what changed after that time
/// (deactivated products are included, so the till can hide them). AsOf is the time to ask from next.
/// </summary>
public sealed record PullProducts(Guid ShopId, Guid DeviceId, DateTimeOffset? Since) : IQuery<ProductPullResponse>;

internal sealed class PullProductsHandler(IDeviceGate gate, IProductPullStore store, TimeProvider clock) : IQueryHandler<PullProducts, ProductPullResponse>
{
    public const int MaxProducts = 5_000;

    public async Task<Result<ProductPullResponse>> Handle(PullProducts q, CancellationToken ct)
    {
        var allowed = await gate.Approved(q.ShopId, q.DeviceId, ct);
        if (!allowed.IsSuccess) return Result<ProductPullResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var asOf = clock.GetUtcNow();
        var products = await store.ChangedSince(q.ShopId, q.Since, MaxProducts, ct);
        // A full page means there may be more: tell the till to ask again from the last change it got, not from now.
        if (products.Count >= MaxProducts) asOf = products.Max(p => p.UpdatedAt);
        return Result<ProductPullResponse>.Ok(new(products, asOf));
    }
}
