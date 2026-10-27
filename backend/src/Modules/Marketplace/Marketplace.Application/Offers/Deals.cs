using Identity.Contracts;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Offers;

public interface IDealStore
{
    Task Add(Deal deal, CancellationToken ct);
    Task<Deal?> Get(Guid dealId, CancellationToken ct);
    Task<List<Deal>> ForSupplier(Guid supplierId, CancellationToken ct);

    /// <summary>Deals that are running now for an area, from suppliers that are trading.</summary>
    Task<List<(Deal Deal, string SupplierName)>> ActiveInArea(string municipalityCode, DateTimeOffset now, CancellationToken ct);

    Task Save(CancellationToken ct);
}

internal static class DealMapping
{
    public static DealTermsDto ToDto(DealTerms t) => new(t.SupplierProductId, t.PercentOff, t.CentsOffPerCase, t.BuyCases, t.FreeCases,
        t.Items?.Select(i => new ComboItemDto(i.SupplierProductId, i.Cases)).ToList(), t.ComboPriceCents);

    public static DealTerms FromDto(DealTermsDto t) => new(t.SupplierProductId, t.PercentOff, t.CentsOffPerCase, t.BuyCases, t.FreeCases,
        t.Items?.Select(i => new ComboItem(i.SupplierProductId, i.Cases)).ToList(), t.ComboPriceCents);

    public static string TypeName(DealType t) => t switch { DealType.FreeGoods => "free_goods", DealType.Combo => "combo", _ => "discount" };

    public static bool TryParseType(string? text, out DealType type)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "discount": type = DealType.Discount; return true;
            case "free_goods": type = DealType.FreeGoods; return true;
            case "combo": type = DealType.Combo; return true;
            default: type = default; return false;
        }
    }

    public static DealResponse ToResponse(Deal d, string supplierName, DateTimeOffset now) => new(
        d.Id, d.SupplierId, supplierName, d.Title, TypeName(d.Type), ToDto(d.Terms), d.StartsAt, d.EndsAt, [.. d.AreaList],
        d.IsSponsored, d.IsSponsored ? "Sponsored" : null, d.IsActiveAt(now));
}

// ---- M2-3: create and cancel ----

public sealed record CreateDeal(Guid SupplierId, CreateDealRequest Request) : ICommand<DealResponse>;

internal sealed class CreateDealHandler(ISupplierStore suppliers, ISupplierProductStore products, IDealStore deals, IOrganisationAccess access, TimeProvider clock)
    : ICommandHandler<CreateDeal, DealResponse>
{
    public async Task<Result<DealResponse>> Handle(CreateDeal c, CancellationToken ct)
    {
        var r = c.Request;
        var supplier = await suppliers.Get(c.SupplierId, ct);
        if (supplier is null) return Result<DealResponse>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, r.CallerPersonId, "deals.manage", ct);
        if (!allowed.IsSuccess) return Result<DealResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (!supplier.IsTrading) return Result<DealResponse>.Fail("supplier.not_verified", "Only a verified supplier can run deals.");
        if (!DealMapping.TryParseType(r.Type, out var type)) return Result<DealResponse>.Fail("deal.type_invalid", "Type is discount, free_goods or combo.");

        var terms = DealMapping.FromDto(r.Terms);

        // Every product a deal names must be one of this supplier's own.
        var own = (await products.List(supplier.Id, ct)).Select(p => p.Id).ToHashSet();
        var named = new[] { terms.SupplierProductId }.Where(id => id is not null).Select(id => id!.Value).Concat(terms.Items?.Select(i => i.SupplierProductId) ?? []);
        if (named.Any(id => !own.Contains(id))) return Result<DealResponse>.Fail("deal.product_unknown", "A product in the deal is not in your catalogue.");

        var areas = (await suppliers.Areas(supplier.Id, ct)).Select(a => a.MunicipalityCode).ToList();
        var created = Deal.Create(supplier.Id, new DealInput(r.Title, type, terms, r.StartsAt, r.EndsAt, r.Areas ?? [], r.IsSponsored), areas, clock.GetUtcNow());
        if (!created.IsSuccess) return Result<DealResponse>.Fail(created.Error!.Code, created.Error.Message);

        await deals.Add(created.Value!, ct);
        await deals.Save(ct);
        return Result<DealResponse>.Ok(DealMapping.ToResponse(created.Value!, supplier.Name, clock.GetUtcNow()));
    }
}

public sealed record CancelDeal(Guid DealId, CancelDealRequest Request) : ICommand<Unit>;

internal sealed class CancelDealHandler(ISupplierStore suppliers, IDealStore deals, IOrganisationAccess access) : ICommandHandler<CancelDeal, Unit>
{
    public async Task<Result<Unit>> Handle(CancelDeal c, CancellationToken ct)
    {
        var deal = await deals.Get(c.DealId, ct);
        if (deal is null) return Result<Unit>.Fail("deal.not_found", "Deal not found.");
        var supplier = await suppliers.Get(deal.SupplierId, ct);
        if (supplier is null) return Result<Unit>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, c.Request.CallerPersonId, "deals.manage", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);

        deal.Cancel();
        await deals.Save(ct);
        return Result<Unit>.Ok(default);
    }
}

public sealed record ListSupplierDeals(Guid SupplierId, Guid CallerPersonId) : IQuery<List<DealResponse>>;

public sealed record BrowseDeals(string MunicipalityCode) : IQuery<List<DealResponse>>;

internal sealed class DealQueries(ISupplierStore suppliers, IDealStore deals, IOrganisationAccess access, TimeProvider clock)
    : IQueryHandler<ListSupplierDeals, List<DealResponse>>, IQueryHandler<BrowseDeals, List<DealResponse>>
{
    public async Task<Result<List<DealResponse>>> Handle(ListSupplierDeals q, CancellationToken ct)
    {
        var supplier = await suppliers.Get(q.SupplierId, ct);
        if (supplier is null) return Result<List<DealResponse>>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, q.CallerPersonId, "deals.manage", ct);
        if (!allowed.IsSuccess) return Result<List<DealResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var now = clock.GetUtcNow();
        return Result<List<DealResponse>>.Ok((await deals.ForSupplier(supplier.Id, ct)).OrderByDescending(d => d.StartsAt).Select(d => DealMapping.ToResponse(d, supplier.Name, now)).ToList());
    }

    /// <summary>GET /deals?area= : what is on offer now in an area. Sponsored deals carry their label and are never listed first for being sponsored.</summary>
    public async Task<Result<List<DealResponse>>> Handle(BrowseDeals q, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var active = await deals.ActiveInArea(q.MunicipalityCode, now, ct);
        return Result<List<DealResponse>>.Ok(active.OrderBy(x => x.Deal.EndsAt).ThenBy(x => x.Deal.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => DealMapping.ToResponse(x.Deal, x.SupplierName, now)).ToList());
    }
}
