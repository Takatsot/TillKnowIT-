using Identity.Contracts;
using Marketplace.Application.Offers;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Orders;

public interface IRatingStore
{
    Task<Rating?> ForOrder(Guid orderId, CancellationToken ct);
    Task Add(Rating rating, CancellationToken ct);
    Task<List<Rating>> ForSupplier(Guid supplierId, CancellationToken ct);
    Task Save(CancellationToken ct);
}

// ---------------------------------------------------------------- M7-1: rate a delivery

public sealed record RateOrder(Guid OrderId, RateOrderRequest Request) : ICommand<RatingResponse>;

internal sealed class RateOrderHandler(IOrderStore orders, IRatingStore ratings, IOrganisationAccess access, TimeProvider clock) : ICommandHandler<RateOrder, RatingResponse>
{
    public async Task<Result<RatingResponse>> Handle(RateOrder c, CancellationToken ct)
    {
        var order = await orders.Get(c.OrderId, ct);
        if (order is null) return Result<RatingResponse>.Fail("order.not_found", "Order not found.");
        if (!await access.Has(c.Request.CallerPersonId, order.ShopId, "stock.receive", ct)) return Result<RatingResponse>.Fail("permission.denied", "You do not have access to this order.");
        if (await ratings.ForOrder(order.Id, ct) is not null) return Result<RatingResponse>.Fail("rating.already_rated", "This delivery has already been rated.");

        var r = c.Request;
        var created = Rating.Create(order, r.CallerPersonId, r.OnTime, r.Complete, r.Score, r.Comment, clock.GetUtcNow());
        if (!created.IsSuccess) return Result<RatingResponse>.Fail(created.Error!.Code, created.Error.Message);

        await ratings.Add(created.Value!, ct);
        await ratings.Save(ct);
        var x = created.Value!;
        return Result<RatingResponse>.Ok(new RatingResponse(x.OrderId, x.OnTime, x.Complete, x.Score, x.Comment, x.CreatedAt));
    }
}

public sealed record GetSupplierRatings(Guid SupplierId, Guid CallerPersonId) : IQuery<SupplierRatingSummary>;

public static class RatingSummary
{
    public static SupplierRatingSummary Of(IReadOnlyCollection<Rating> ratings)
    {
        var enough = ratings.Count >= Rating.MinRatingsToShow;
        return new SupplierRatingSummary(
            ratings.Count,
            enough ? Math.Round(ratings.Average(r => r.Score), 1) : null,
            enough ? (int)Math.Round(ratings.Count(r => r.OnTime) * 100.0 / ratings.Count) : null,
            enough ? (int)Math.Round(ratings.Count(r => r.Complete) * 100.0 / ratings.Count) : null,
            ratings.Where(r => r.Comment is not null).OrderByDescending(r => r.CreatedAt).Take(10).Select(r => r.Comment!).ToList());
    }
}

internal sealed class GetSupplierRatingsHandler(ISupplierStore suppliers, IRatingStore ratings, IOrganisationAccess access) : IQueryHandler<GetSupplierRatings, SupplierRatingSummary>
{
    public async Task<Result<SupplierRatingSummary>> Handle(GetSupplierRatings q, CancellationToken ct)
    {
        var supplier = await suppliers.Get(q.SupplierId, ct);
        if (supplier is null) return Result<SupplierRatingSummary>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, q.CallerPersonId, "supplier.orders", ct);
        if (!allowed.IsSuccess) return Result<SupplierRatingSummary>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<SupplierRatingSummary>.Ok(RatingSummary.Of(await ratings.ForSupplier(supplier.Id, ct)));
    }
}

// ---------------------------------------------------------------- M7-2: demand totals

/// <summary>One opted-in shop, as the demand calculation sees it: where it is and which barcodes it is low on. Nothing else about it is passed on.</summary>
public sealed record DemandShop(string AreaCode, HashSet<string> LowBarcodes);

public static class DemandCalculator
{
    /// <summary>Never lower than this, whatever the setting says: below it a count could single out a shop.</summary>
    public const int FloorMinShops = 3;

    public static DemandResponse Compute(IEnumerable<DemandShop> shops, IReadOnlyCollection<(Guid Id, string Name, string Barcode)> products, IReadOnlyCollection<string> supplierAreas, int minShops)
    {
        var min = Math.Max(FloorMinShops, minShops);
        var byArea = shops.Where(s => supplierAreas.Contains(s.AreaCode)).GroupBy(s => s.AreaCode);

        var areas = new List<DemandArea>();
        foreach (var area in byArea.OrderBy(a => a.Key))
        {
            var rows = products
                .Select(p => new DemandProduct(p.Id, p.Name, area.Count(s => s.LowBarcodes.Contains(p.Barcode))))
                .Where(p => p.ShopsLow >= min)
                .OrderByDescending(p => p.ShopsLow).ThenBy(p => p.Name).ToList();
            if (rows.Count > 0) areas.Add(new DemandArea(area.Key, rows));
        }
        return new DemandResponse(min, areas);
    }
}

public sealed record GetDemand(Guid SupplierId, Guid CallerPersonId) : IQuery<DemandResponse>;

/// <summary>
/// A shop counts only if its owner chose to share: with every supplier ("all") or with their usual suppliers ("usual", and this
/// supplier is one of them). The default is not to share, so a shop that never touched the setting is never counted.
/// </summary>
internal sealed class GetDemandHandler(ISupplierStore suppliers, ISupplierProductStore products, IShopDirectory shops, IShopMarketplaceStore settings, IShopStockInfo stock, IOrganisationAccess access)
    : IQueryHandler<GetDemand, DemandResponse>
{
    public const string MinShopsKey = "demand.min_shops";

    public async Task<Result<DemandResponse>> Handle(GetDemand q, CancellationToken ct)
    {
        var supplier = await suppliers.Get(q.SupplierId, ct);
        if (supplier is null) return Result<DemandResponse>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, q.CallerPersonId, "supplier.orders", ct);
        if (!allowed.IsSuccess) return Result<DemandResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var areas = (await suppliers.Areas(supplier.Id, ct)).Select(a => a.MunicipalityCode).ToList();
        var catalogue = (await products.List(supplier.Id, ct)).Where(p => p.Barcode is not null).Select(p => (p.Id, p.Name, Barcode: p.Barcode!)).ToList();
        var minShops = int.TryParse((await settings.Settings(ct)).GetValueOrDefault(MinShopsKey), out var configured) ? configured : DemandCalculator.FloorMinShops;

        var counted = new List<DemandShop>();
        foreach (var shopId in await shops.VerifiedShopIds(ct))
        {
            var shop = await shops.Get(shopId, ct);
            if (shop?.MunicipalityCode is not { } area || !areas.Contains(area)) continue;

            var mode = (await settings.GetSharing(shopId, ct))?.Mode ?? SharingMode.Off;
            var optedIn = mode == SharingMode.All || (mode == SharingMode.Usual && await settings.GetShopSupplier(shopId, supplier.Id, ct) is not null);
            if (!optedIn) continue;

            var low = (await stock.NeedingReorder(shopId, ct)).Where(p => p.Barcode is not null).Select(p => p.Barcode!).ToHashSet();
            counted.Add(new DemandShop(area, low));
        }
        return Result<DemandResponse>.Ok(DemandCalculator.Compute(counted, catalogue, areas, minShops));
    }
}
