using Identity.Contracts;
using Marketplace.Contracts;
using Marketplace.Domain;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Offers;

/// <summary>A supplier's listing for a product the shop needs, before any ranking: verified, in stock, and delivering to the shop's municipality.</summary>
public sealed record OfferSource(
    Guid SupplierId, string SupplierName, long MinOrderCents, long DeliveryFeeCents, Guid SupplierProductId, int UnitsPerCase,
    long CasePriceCents, bool VatIncluded, DeliveryDays Days, double? Rating);

public interface IOfferStore
{
    Task<List<OfferSource>> Sources(string barcode, string municipalityCode, CancellationToken ct);
    Task<List<Deal>> ActiveDeals(IEnumerable<Guid> supplierIds, string municipalityCode, DateTimeOffset now, CancellationToken ct);
    Task<List<ShopSupplier>> ShopSuppliers(Guid shopId, CancellationToken ct);
    Task<Dictionary<string, string>> Settings(CancellationToken ct);
}

/// <summary>Puts the pieces together: listings, running deals, the shop's usual suppliers and the ranking settings.</summary>
internal static class OfferBuilder
{
    public static async Task<List<RankedOffer>> Build(
        IOfferStore store, ShopProductInfo product, string municipality, Guid shopId, DateTimeOffset now, DateOnly today, RankingWeights weights, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(product.Barcode)) return [];   // without a barcode there is nothing to match suppliers' products on

        var sources = await store.Sources(product.Barcode, municipality, ct);
        if (sources.Count == 0) return [];

        var deals = await store.ActiveDeals(sources.Select(s => s.SupplierId).Distinct(), municipality, now, ct);
        var usual = (await store.ShopSuppliers(shopId, ct)).ToDictionary(s => s.SupplierId);
        var quantity = Math.Max(product.SuggestedOrderQuantity, 1);

        var candidates = sources.Select(s =>
        {
            // If a supplier has several deals on this product, the one that costs least for this quantity applies.
            var cases = Math.Max(1, (int)Math.Ceiling(quantity / (double)s.UnitsPerCase));
            var deal = deals.Where(d => d.SupplierId == s.SupplierId && d.Type != DealType.Combo && d.Terms.SupplierProductId == s.SupplierProductId)
                .OrderBy(d => DealCalculator.Apply(s.CasePriceCents, cases, d.Type, d.Terms) is var e ? e.TotalCostCents / (double)e.CasesReceived : 0)
                .FirstOrDefault();
            usual.TryGetValue(s.SupplierId, out var shopSupplier);

            return new OfferCandidate(s.SupplierId, s.SupplierName, s.SupplierProductId, s.UnitsPerCase, s.CasePriceCents, s.VatIncluded,
                s.DeliveryFeeCents, s.MinOrderCents, IsUsual: shopSupplier is not null, shopSupplier?.LastPriceCents, s.Rating,
                SupplierArea.DaysUntilNext(s.Days, today), deal?.Type, deal?.Terms, deal?.Title, deal?.IsSponsored ?? false);
        });

        return OfferEngine.Rank(candidates, quantity, product.PriceCents > 0 ? product.PriceCents : null, weights);
    }

    public static OfferResponse ToResponse(RankedOffer o) => new(
        o.Position, o.SupplierId, o.SupplierName, o.SupplierProductId, o.Cases, o.UnitsReceived, o.TotalCostCents, o.TrueCostPerUnitCents,
        o.MarginPercent, o.IsCheapest, o.IsUsual, o.LastPriceCents, o.MeetsMinimum, o.DeliveryDaysAway, o.Rating, o.DealTitle, o.Label);
}

/// <summary>M3-1: ranked options for reordering one product.</summary>
public sealed record GetOffers(Guid ShopId, Guid CallerPersonId, Guid ProductId) : IQuery<OffersResponse>;

/// <summary>M3-1: the order list. Every product that is low, out but selling, or expiring, with its best option and the cheapest.</summary>
public sealed record GetOrderList(Guid ShopId, Guid CallerPersonId) : IQuery<List<OrderListItem>>;

internal sealed class OfferHandlers(
    IOfferStore store, IShopStockInfo stock, IShopDirectory shops, IOrganisationAccess access, TimeProvider clock)
    : IQueryHandler<GetOffers, OffersResponse>, IQueryHandler<GetOrderList, List<OrderListItem>>
{
    public async Task<Result<OffersResponse>> Handle(GetOffers q, CancellationToken ct)
    {
        var context = await Context(q.ShopId, q.CallerPersonId, ct);
        if (!context.IsSuccess) return Result<OffersResponse>.Fail(context.Error!.Code, context.Error.Message);
        var (municipality, weights) = context.Value;

        var product = await stock.GetProduct(q.ShopId, q.ProductId, ct);
        if (product is null) return Result<OffersResponse>.Fail("product.not_found", "Product not found.");

        var now = clock.GetUtcNow();
        var offers = await OfferBuilder.Build(store, product, municipality, q.ShopId, now, DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(2)).DateTime), weights, ct);
        return Result<OffersResponse>.Ok(new OffersResponse(product.ProductId, product.Name, Math.Max(product.SuggestedOrderQuantity, 1),
            product.PriceCents > 0 ? product.PriceCents : null, offers.Select(OfferBuilder.ToResponse).ToList()));
    }

    public async Task<Result<List<OrderListItem>>> Handle(GetOrderList q, CancellationToken ct)
    {
        var context = await Context(q.ShopId, q.CallerPersonId, ct);
        if (!context.IsSuccess) return Result<List<OrderListItem>>.Fail(context.Error!.Code, context.Error.Message);
        var (municipality, weights) = context.Value;

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(2)).DateTime);
        var items = new List<OrderListItem>();
        foreach (var product in await stock.NeedingReorder(q.ShopId, ct))
        {
            var offers = await OfferBuilder.Build(store, product, municipality, q.ShopId, now, today, weights, ct);
            items.Add(new OrderListItem(product.ProductId, product.Name, product.OnHand, product.DaysLeft, Math.Max(product.SuggestedOrderQuantity, 1), product.Flags,
                offers.Count, offers.FirstOrDefault() is { } best ? OfferBuilder.ToResponse(best) : null,
                offers.FirstOrDefault(o => o.IsCheapest) is { } cheapest ? OfferBuilder.ToResponse(cheapest) : null));
        }
        return Result<List<OrderListItem>>.Ok(items);
    }

    /// <summary>The caller must be allowed to receive stock in this shop, and the shop must have a municipality so we know who delivers to it.</summary>
    private async Task<Result<(string Municipality, RankingWeights Weights)>> Context(Guid shopId, Guid caller, CancellationToken ct)
    {
        if (!await access.Has(caller, shopId, "stock.receive", ct))
            return Result<(string, RankingWeights)>.Fail("permission.denied", "You do not have access to this shop.");
        var shop = await shops.Get(shopId, ct);
        if (shop is null) return Result<(string, RankingWeights)>.Fail("shop.not_found", "Shop not found.");
        if (string.IsNullOrWhiteSpace(shop.MunicipalityCode))
            return Result<(string, RankingWeights)>.Fail("offers.no_municipality", "Tell us which municipality your shop is in so we can find suppliers that deliver to you.");
        return Result<(string, RankingWeights)>.Ok((shop.MunicipalityCode, RankingWeights.From(await store.Settings(ct))));
    }
}
