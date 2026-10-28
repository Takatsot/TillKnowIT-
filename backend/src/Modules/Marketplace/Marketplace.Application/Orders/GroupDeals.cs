using Identity.Contracts;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Orders;

public interface IGroupDealStore
{
    Task Add(GroupDeal deal, CancellationToken ct);

    /// <summary>With its commitments.</summary>
    Task<GroupDeal?> Get(Guid dealId, CancellationToken ct);

    Task<List<GroupDeal>> OpenInArea(string areaCode, CancellationToken ct);
    Task<List<GroupDeal>> ForSupplier(Guid supplierId, CancellationToken ct);

    /// <summary>Every deal made for these buying groups, in any status.</summary>
    Task<List<GroupDeal>> ForBuyingGroups(IReadOnlyCollection<Guid> groupIds, CancellationToken ct);

    /// <summary>Open deals past their closing time, plus reached deals that still have a shop without its order.</summary>
    Task<List<GroupDeal>> NeedingClosure(DateTimeOffset now, int take, CancellationToken ct);

    Task Save(CancellationToken ct);
}

internal static class GroupMapping
{
    /// <param name="forShop">The shop looking, whose own quantity is shown. Null for the supplier, who sees how many shops joined.</param>
    public static GroupDealResponse ToResponse(GroupDeal d, string supplierName, SupplierProduct? product, Guid? forShop) => new(
        d.Id, d.SupplierId, supplierName, d.SupplierProductId, product?.Name ?? "", product?.UnitsPerCase ?? 1, d.BulkPriceCents, d.BulkPriceCents,
        d.MinTotalCases, d.TotalCases, (int)Math.Min(100, d.TotalCases * 100L / d.MinTotalCases), d.ClosesAt, d.AreaCode, d.Status.ToString().ToLowerInvariant(),
        forShop is { } shop ? d.CasesOf(shop) : null, forShop is null ? d.Commitments.Count : null, d.BuyingGroupId);
}

// ---------------------------------------------------------------- M6-1: a supplier posts a group deal

public sealed record PostGroupDeal(Guid SupplierId, PostGroupDealRequest Request) : ICommand<GroupDealResponse>;

internal sealed class PostGroupDealHandler(ISupplierStore suppliers, ISupplierProductStore products, IGroupDealStore groups, IOrganisationAccess access, TimeProvider clock, IBuyingGroupStore? buying = null)
    : ICommandHandler<PostGroupDeal, GroupDealResponse>
{
    public async Task<Result<GroupDealResponse>> Handle(PostGroupDeal c, CancellationToken ct)
    {
        var r = c.Request;
        var supplier = await suppliers.Get(c.SupplierId, ct);
        if (supplier is null) return Result<GroupDealResponse>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, r.CallerPersonId, "deals.manage", ct);
        if (!allowed.IsSuccess) return Result<GroupDealResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (!supplier.IsTrading) return Result<GroupDealResponse>.Fail("supplier.not_verified", "Only a verified supplier can post group deals.");

        var product = await products.Get(r.SupplierProductId, ct);
        if (product is null || product.SupplierId != supplier.Id) return Result<GroupDealResponse>.Fail("group.product_unknown", "That product is not in your catalogue.");

        if (r.BuyingGroupId is { } groupId && (buying is null || await buying.Get(groupId, ct) is not { IsActive: true }))
            return Result<GroupDealResponse>.Fail("buying_group.not_found", "That buying group does not exist.");

        var areas = (await suppliers.Areas(supplier.Id, ct)).Select(a => a.MunicipalityCode).ToList();
        var created = GroupDeal.Create(supplier.Id, new GroupDealInput(product.Id, r.BulkPriceCents, r.MinTotalCases, r.ClosesAt, r.AreaCode, r.BuyingGroupId), areas, clock.GetUtcNow());
        if (!created.IsSuccess) return Result<GroupDealResponse>.Fail(created.Error!.Code, created.Error.Message);

        await groups.Add(created.Value!, ct);
        await groups.Save(ct);
        return Result<GroupDealResponse>.Ok(GroupMapping.ToResponse(created.Value!, supplier.Name, product, null));
    }
}

public sealed record CancelGroupDeal(Guid DealId, Guid CallerPersonId) : ICommand<Unit>;
public sealed record CloseGroupDealEarly(Guid DealId, Guid CallerPersonId) : ICommand<Unit>;

internal sealed class ManageGroupDealHandlers(ISupplierStore suppliers, IGroupDealStore groups, IOrganisationAccess access, GroupDealClosing closing)
    : ICommandHandler<CancelGroupDeal, Unit>, ICommandHandler<CloseGroupDealEarly, Unit>
{
    public async Task<Result<Unit>> Handle(CancelGroupDeal c, CancellationToken ct)
    {
        var (deal, supplier, error) = await Load(c.DealId, c.CallerPersonId, ct);
        if (error is not null) return Result<Unit>.Fail(error.Code, error.Message);
        var cancelled = deal!.Cancel();
        if (!cancelled.IsSuccess) return Result<Unit>.Fail(cancelled.Error!.Code, cancelled.Error.Message);
        await groups.Save(ct);
        await closing.TellEveryone(deal, supplier!.Name, "cancelled", ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Unit>> Handle(CloseGroupDealEarly c, CancellationToken ct)
    {
        var (deal, _, error) = await Load(c.DealId, c.CallerPersonId, ct);
        if (error is not null) return Result<Unit>.Fail(error.Code, error.Message);
        var closed = deal!.CloseEarly();
        if (!closed.IsSuccess) return Result<Unit>.Fail(closed.Error!.Code, closed.Error.Message);
        await groups.Save(ct);   // the closing job makes the orders on its next round
        return Result<Unit>.Ok(default);
    }

    private async Task<(GroupDeal?, Supplier?, Error?)> Load(Guid dealId, Guid caller, CancellationToken ct)
    {
        var deal = await groups.Get(dealId, ct);
        if (deal is null) return (null, null, new Error("group.not_found", "Group deal not found."));
        var supplier = (await suppliers.Get(deal.SupplierId, ct))!;
        var allowed = await SupplierAccess.Check(access, supplier, caller, "deals.manage", ct);
        return allowed.IsSuccess ? (deal, supplier, null) : (null, null, allowed.Error);
    }
}

// ---------------------------------------------------------------- M6-2: a shop joins

public sealed record JoinGroupDeal(Guid DealId, Guid ShopId, JoinGroupDealRequest Request) : ICommand<GroupDealResponse>;

internal sealed class JoinGroupDealHandler(IGroupDealStore groups, ISupplierStore suppliers, ISupplierProductStore products, IShopDirectory shops, IOrganisationAccess access, TimeProvider clock, IBuyingGroupStore? buying = null)
    : ICommandHandler<JoinGroupDeal, GroupDealResponse>
{
    public async Task<Result<GroupDealResponse>> Handle(JoinGroupDeal c, CancellationToken ct)
    {
        if (!await access.Has(c.Request.CallerPersonId, c.ShopId, "stock.receive", ct)) return Result<GroupDealResponse>.Fail("permission.denied", "You do not have access to this shop.");
        var deal = await groups.Get(c.DealId, ct);
        if (deal is null) return Result<GroupDealResponse>.Fail("group.not_found", "Group deal not found.");

        if (deal.BuyingGroupId is { } groupId)
        {
            // An invite-only deal: members of that buying group only, wherever their shops are. Others are told it does not exist.
            if (buying is null || await buying.Member(groupId, c.ShopId, ct) is not { IsMember: true }) return Result<GroupDealResponse>.Fail("group.not_found", "Group deal not found.");
        }
        else
        {
            var shop = await shops.Get(c.ShopId, ct);
            if (shop is null || shop.MunicipalityCode != deal.AreaCode) return Result<GroupDealResponse>.Fail("group.not_in_area", "This group deal is for shops in another area.");
        }

        var committed = deal.Commit(c.ShopId, c.Request.CallerPersonId, c.Request.Cases, clock.GetUtcNow());
        if (!committed.IsSuccess) return Result<GroupDealResponse>.Fail(committed.Error!.Code, committed.Error.Message);
        await groups.Save(ct);

        var supplier = (await suppliers.Get(deal.SupplierId, ct))!;
        return Result<GroupDealResponse>.Ok(GroupMapping.ToResponse(deal, supplier.Name, await products.Get(deal.SupplierProductId, ct), c.ShopId));
    }
}

// ---------------------------------------------------------------- browsing

/// <summary>Open group deals in the shop's own area, each with the shop's own quantity.</summary>
public sealed record ListGroupDeals(Guid ShopId, Guid CallerPersonId) : IQuery<List<GroupDealResponse>>;

public sealed record ListSupplierGroupDeals(Guid SupplierId, Guid CallerPersonId) : IQuery<List<GroupDealResponse>>;

internal sealed class GroupDealQueries(IGroupDealStore groups, ISupplierStore suppliers, ISupplierProductStore products, IShopDirectory shops, IOrganisationAccess access, IBuyingGroupStore? buying = null)
    : IQueryHandler<ListGroupDeals, List<GroupDealResponse>>, IQueryHandler<ListSupplierGroupDeals, List<GroupDealResponse>>
{
    public async Task<Result<List<GroupDealResponse>>> Handle(ListGroupDeals q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.ShopId, "stock.receive", ct)) return Result<List<GroupDealResponse>>.Fail("permission.denied", "You do not have access to this shop.");
        var shop = await shops.Get(q.ShopId, ct);
        var visible = shop?.MunicipalityCode is { } area ? (await groups.OpenInArea(area, ct)).Where(d => d.BuyingGroupId is null).ToList() : [];
        if (buying is not null)
        {
            var mine = await buying.GroupIdsOf(q.ShopId, ct);
            if (mine.Count > 0) visible.AddRange((await groups.ForBuyingGroups(mine, ct)).Where(d => d.Status == GroupDealStatus.Open));
        }

        var list = new List<GroupDealResponse>();
        foreach (var d in visible.OrderBy(d => d.ClosesAt))
        {
            var supplier = await suppliers.Get(d.SupplierId, ct);
            if (supplier is null || !supplier.IsTrading) continue;
            list.Add(GroupMapping.ToResponse(d, supplier.Name, await products.Get(d.SupplierProductId, ct), q.ShopId));
        }
        return Result<List<GroupDealResponse>>.Ok(list);
    }

    public async Task<Result<List<GroupDealResponse>>> Handle(ListSupplierGroupDeals q, CancellationToken ct)
    {
        var supplier = await suppliers.Get(q.SupplierId, ct);
        if (supplier is null) return Result<List<GroupDealResponse>>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, q.CallerPersonId, "deals.manage", ct);
        if (!allowed.IsSuccess) return Result<List<GroupDealResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var list = new List<GroupDealResponse>();
        foreach (var d in (await groups.ForSupplier(supplier.Id, ct)).OrderByDescending(d => d.ClosesAt))
            list.Add(GroupMapping.ToResponse(d, supplier.Name, await products.Get(d.SupplierProductId, ct), null));
        return Result<List<GroupDealResponse>>.Ok(list);
    }
}

// ---------------------------------------------------------------- M6-3: closing

/// <summary>Makes the orders for a reached group and tells the shops when it fails or is cancelled.</summary>
public sealed class GroupDealClosing(IGroupDealStore groups, ISupplierStore suppliers, ISupplierProductStore products, IOrderStore orders, OrderNotifier notifier, TimeProvider clock)
{
    public async Task<GroupClosing> Close(GroupDeal deal, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var supplier = (await suppliers.Get(deal.SupplierId, ct))!;

        if (deal.Status == GroupDealStatus.Open)
        {
            var due = deal.CloseDue(now);
            if (!due.IsSuccess) return new GroupClosing(deal.Id, "not_due", 0);
            await groups.Save(ct);
        }

        if (deal.Status == GroupDealStatus.Failed)
        {
            await TellEveryone(deal, supplier.Name, "failed", ct);
            return new GroupClosing(deal.Id, "failed", 0);
        }
        if (deal.Status != GroupDealStatus.Reached) return new GroupClosing(deal.Id, deal.Status.ToString().ToLowerInvariant(), 0);

        // One order per shop, at the bulk price. Safe to run again: a shop that already has its order is skipped.
        var product = (await products.Get(deal.SupplierProductId, ct))!;
        var payment = supplier.PaymentTerms == PaymentTerms.InApp ? OrderPaymentMethod.InApp : OrderPaymentMethod.OnDelivery;
        var made = 0;
        foreach (var commitment in deal.Commitments.Where(c => c.OrderId is null))
        {
            var line = new PurchaseOrderLine(product.Id, product.Name, product.Barcode, product.UnitsPerCase, commitment.Cases, deal.BulkPriceCents, product.VatIncluded);
            var created = PurchaseOrder.Create(commitment.ShopId, supplier.Id, commitment.CreatedBy ?? Guid.Empty, [line], payment, 0, supplier.DeliveryFeeCents, now);
            if (!created.IsSuccess) continue;   // left without an order, so the next round tries again
            var order = created.Value!;
            order.Send(OrderChannel.App, now);
            await orders.Add(order, ct);
            commitment.AttachOrder(order.Id);
            made++;
        }
        await orders.Save(ct);
        await groups.Save(ct);

        foreach (var commitment in deal.Commitments.Where(c => c.OrderId is not null))
            await notifier.ToShopOwner(commitment.ShopId, "group.update", new Dictionary<string, string>
            {
                ["supplier"] = supplier.Name, ["product"] = product.Name,
                ["summary"] = $"The group reached its minimum. Your order for {commitment.Cases} cases at {Rands.Rand(deal.BulkPriceCents)} a case has been sent to {supplier.Name}.",
            }, $"group:{deal.Id:N}:reached:{commitment.ShopId:N}", ct);
        return new GroupClosing(deal.Id, "reached", made);
    }

    public async Task TellEveryone(GroupDeal deal, string supplierName, string outcome, CancellationToken ct)
    {
        var product = await products.Get(deal.SupplierProductId, ct);
        var text = outcome == "cancelled"
            ? $"{supplierName} cancelled this group deal. Nothing was charged."
            : $"The group did not reach its minimum of {deal.MinTotalCases} cases. Nothing was charged.";
        foreach (var commitment in deal.Commitments)
            await notifier.ToShopOwner(commitment.ShopId, "group.update", new Dictionary<string, string>
            {
                ["supplier"] = supplierName, ["product"] = product?.Name ?? "", ["summary"] = text,
            }, $"group:{deal.Id:N}:{outcome}:{commitment.ShopId:N}", ct);
    }
}

public sealed record CloseDueGroupDeals : ICommand<List<GroupClosing>>;

/// <summary>The worker's job, every few minutes.</summary>
internal sealed class CloseDueGroupDealsHandler(IGroupDealStore groups, GroupDealClosing closing, TimeProvider clock) : ICommandHandler<CloseDueGroupDeals, List<GroupClosing>>
{
    public async Task<Result<List<GroupClosing>>> Handle(CloseDueGroupDeals c, CancellationToken ct)
    {
        var done = new List<GroupClosing>();
        foreach (var deal in await groups.NeedingClosure(clock.GetUtcNow(), 20, ct))
            done.Add(await closing.Close(deal, ct));
        return Result<List<GroupClosing>>.Ok(done);
    }
}
