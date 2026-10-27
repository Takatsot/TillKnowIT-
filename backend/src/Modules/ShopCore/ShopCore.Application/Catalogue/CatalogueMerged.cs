using Catalogue.Contracts;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Catalogue;

public interface IProductRepointStore
{
    /// <summary>Moves every shop product that pointed at the old catalogue entry to the new one. Safe to run twice.</summary>
    Task<int> Repoint(Guid fromCatalogueProductId, Guid toCatalogueProductId, DateTimeOffset now, CancellationToken ct);
}

/// <summary>E2-3: when an admin merges a suggestion into an approved product, shops that used the suggestion follow it.</summary>
internal sealed class CatalogueProductMergedHandler(IProductRepointStore store, TimeProvider clock) : IDomainEventHandler<CatalogueProductMerged>
{
    public Task Handle(CatalogueProductMerged e, CancellationToken ct) => store.Repoint(e.FromId, e.IntoId, clock.GetUtcNow(), ct);
}
