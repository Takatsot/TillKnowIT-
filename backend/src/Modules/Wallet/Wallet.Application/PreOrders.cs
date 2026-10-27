using System.Globalization;
using System.Text.RegularExpressions;
using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

// ---------------------------------------------------------------- W4-2: the menu and the breaks

public sealed record SaveBreaks(Guid SchoolId, SaveBreaksRequest Request) : ICommand<Unit>;
public sealed record AddMenuItem(Guid SchoolId, SaveMenuItemRequest Request) : ICommand<MenuItemDto>;
public sealed record ChangeMenuItem(Guid ItemId, SaveMenuItemRequest Request) : ICommand<MenuItemDto>;
public sealed record GetSchoolMenu(Guid SchoolId, Guid CallerPersonId) : IQuery<MenuResponse>;
public sealed record GetMenuForChild(Guid HolderId, Guid CallerPersonId) : IQuery<MenuResponse>;

internal sealed partial class MenuHandlers(IWalletStore store, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<SaveBreaks, Unit>, ICommandHandler<AddMenuItem, MenuItemDto>, ICommandHandler<ChangeMenuItem, MenuItemDto>,
      IQueryHandler<GetSchoolMenu, MenuResponse>, IQueryHandler<GetMenuForChild, MenuResponse>
{
    public async Task<Result<Unit>> Handle(SaveBreaks c, CancellationToken ct)
    {
        var school = await store.GetSchool(c.SchoolId, ct);
        if (school is null) return Result<Unit>.Fail("school.not_found", "School not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, school.Id, c.Request.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var slots = new List<BreakSlot>();
        foreach (var b in c.Request.Breaks ?? [])
        {
            if (!Code().IsMatch(b.Code ?? "")) return Result<Unit>.Fail("break.code_invalid", "A break code is lowercase letters, digits, - or _, up to 20 characters.");
            if (string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 50) return Result<Unit>.Fail("break.name_invalid", "Give each break a name.");
            if (!TimeOnly.TryParseExact(b.CutOff, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var cutOff)) return Result<Unit>.Fail("break.cutoff_invalid", "The cut-off time is written like 09:30.");
            if (slots.Any(s => s.Code == b.Code)) return Result<Unit>.Fail("break.duplicate", "Each break code can be used once.");
            slots.Add(new BreakSlot(school.Id, b.Code!, b.Name, cutOff, clock.GetUtcNow()));
        }
        if (slots.Count > 10) return Result<Unit>.Fail("break.too_many", "A school has at most 10 breaks.");

        await store.ReplaceBreaks(school.Id, slots, ct);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<MenuItemDto>> Handle(AddMenuItem c, CancellationToken ct)
    {
        var school = await store.GetSchool(c.SchoolId, ct);
        if (school is null) return Result<MenuItemDto>.Fail("school.not_found", "School not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, school.Id, c.Request.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<MenuItemDto>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var valid = MenuItem.Validate(c.Request.Name, c.Request.PriceCents);
        if (!valid.IsSuccess) return Result<MenuItemDto>.Fail(valid.Error!.Code, valid.Error.Message);

        var item = new MenuItem(school.Id, c.Request.Name, c.Request.PriceCents, Clean(c.Request.ProductId), Clean(c.Request.CategoryCode), clock.GetUtcNow());
        if (!c.Request.IsAvailable) item.Change(item.Name, item.PriceCents, false);
        await store.Add(item, ct);
        await store.Save(ct);
        return Result<MenuItemDto>.Ok(ToDto(item));
    }

    public async Task<Result<MenuItemDto>> Handle(ChangeMenuItem c, CancellationToken ct)
    {
        var item = await store.GetMenuItem(c.ItemId, ct);
        if (item is null) return Result<MenuItemDto>.Fail("menu.not_found", "Menu item not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, item.SchoolId, c.Request.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<MenuItemDto>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var valid = MenuItem.Validate(c.Request.Name, c.Request.PriceCents);
        if (!valid.IsSuccess) return Result<MenuItemDto>.Fail(valid.Error!.Code, valid.Error.Message);

        item.Change(c.Request.Name, c.Request.PriceCents, c.Request.IsAvailable);
        await store.Save(ct);
        return Result<MenuItemDto>.Ok(ToDto(item));
    }

    public async Task<Result<MenuResponse>> Handle(GetSchoolMenu q, CancellationToken ct)
    {
        var allowed = await SchoolAccess.Admin(store, permissions, q.SchoolId, q.CallerPersonId, ct);
        return allowed.IsSuccess ? Result<MenuResponse>.Ok(await Menu(q.SchoolId, includeUnavailable: true, ct)) : Result<MenuResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
    }

    /// <summary>A parent sees the menu of their own child's school, and only what is on offer.</summary>
    public async Task<Result<MenuResponse>> Handle(GetMenuForChild q, CancellationToken ct)
    {
        if (await store.GuardianshipOf(q.CallerPersonId, q.HolderId, ct) is null) return Result<MenuResponse>.Fail("permission.denied", "You are not linked to this child.");
        var holder = (await store.GetHolder(q.HolderId, ct))!;
        return Result<MenuResponse>.Ok(await Menu(holder.SchoolId, includeUnavailable: false, ct));
    }

    private async Task<MenuResponse> Menu(Guid schoolId, bool includeUnavailable, CancellationToken ct) => new(
        (await store.Breaks(schoolId, ct)).OrderBy(b => b.CutOff).Select(b => new BreakDto(b.Code, b.Name, b.CutOff.ToString("HH:mm", CultureInfo.InvariantCulture))).ToList(),
        (await store.MenuOf(schoolId, ct)).Where(i => includeUnavailable || i.IsAvailable).OrderBy(i => i.Name).Select(ToDto).ToList());

    private static MenuItemDto ToDto(MenuItem i) => new(i.Id, i.Name, i.PriceCents, i.ProductId, i.CategoryCode, i.IsAvailable);

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    [GeneratedRegex(@"^[a-z0-9_-]{1,20}$")]
    private static partial Regex Code();
}

// ---------------------------------------------------------------- W4-2: a parent pre-orders lunch

public sealed record PlacePreOrder(PlacePreOrderRequest Request) : ICommand<PreOrderResponse>;
public sealed record CancelPreOrder(Guid PreOrderId, Guid CallerPersonId) : ICommand<Unit>;
public sealed record ListChildPreOrders(Guid HolderId, Guid CallerPersonId) : IQuery<List<PreOrderResponse>>;

internal sealed class ParentPreOrderHandlers(IWalletStore store, PurchasePoster poster, TimeProvider clock)
    : ICommandHandler<PlacePreOrder, PreOrderResponse>, ICommandHandler<CancelPreOrder, Unit>, IQueryHandler<ListChildPreOrders, List<PreOrderResponse>>
{
    public const int MaxLines = 10;
    public const int MaxQuantity = 20;

    public async Task<Result<PreOrderResponse>> Handle(PlacePreOrder c, CancellationToken ct)
    {
        var r = c.Request;
        if (await store.GuardianshipOf(r.CallerPersonId, r.HolderId, ct) is null) return Result<PreOrderResponse>.Fail("permission.denied", "You are not linked to this child.");
        var holder = (await store.GetHolder(r.HolderId, ct))!;
        var school = (await store.GetSchool(holder.SchoolId, ct))!;
        var wallet = (await store.WalletOfHolder(holder.Id, ct))!;
        if (!school.IsTrading) return Result<PreOrderResponse>.Fail("school.not_active", "This school is not active yet.");

        var slot = (await store.Breaks(school.Id, ct)).FirstOrDefault(b => b.Code == r.BreakCode?.Trim().ToLowerInvariant());
        if (slot is null) return Result<PreOrderResponse>.Fail("preorder.break_unknown", "That break does not exist.");
        if (r.Lines is null || r.Lines.Count == 0 || r.Lines.Count > MaxLines) return Result<PreOrderResponse>.Fail("preorder.lines_invalid", $"Choose from 1 to {MaxLines} items.");

        // Prices and names come from the menu, never from the request: a parent's phone cannot set what lunch costs.
        var menu = (await store.MenuOf(school.Id, ct)).ToDictionary(i => i.Id);
        var lines = new List<PreOrderLineDto>();
        var items = new List<SaleItem>();
        foreach (var l in r.Lines)
        {
            if (l.Quantity is < 1 or > MaxQuantity) return Result<PreOrderResponse>.Fail("preorder.quantity_invalid", $"Order from 1 to {MaxQuantity} of each item.");
            if (!menu.TryGetValue(l.MenuItemId, out var item) || !item.IsAvailable) return Result<PreOrderResponse>.Fail("preorder.item_unavailable", "One of those items is not on the menu today.");
            if (lines.Any(x => x.MenuItemId == item.Id)) return Result<PreOrderResponse>.Fail("preorder.item_repeated", "List each item once, with its quantity.");
            lines.Add(new PreOrderLineDto(item.Id, item.Name, l.Quantity, item.PriceCents));
            items.Add(new SaleItem(item.ProductId, item.CategoryCode, item.Name, l.Quantity, item.PriceCents));
        }
        var total = lines.Sum(l => l.PriceCents * l.Quantity);

        var now = clock.GetUtcNow();
        var purchaseId = Guid.CreateVersion7();
        var placed = PreOrder.Place(holder.Id, wallet.Id, school.Id, school.ShopId, slot, r.CollectOn, PreOrderMapping.Write(lines), total, purchaseId, now);
        if (!placed.IsSuccess) return Result<PreOrderResponse>.Fail(placed.Error!.Code, placed.Error.Message);

        // The wallet's blocked items and balance apply. The daily limit does not: it is for spending at the till, and the cut-off bounds this.
        var context = new SpendContext(holder.Status, wallet.Status, null, null, 0, await store.Balance(wallet.AccountId, ct), await store.Restrictions(wallet.Id, ct));
        var allowed = SpendRules.Check(context, total, items);
        if (!allowed.IsSuccess) return Result<PreOrderResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var posted = await poster.Post(school, holder, wallet, purchaseId, total, items, null, null, now, offline: false, staffAccount: false, ct, saveNow: false);
        if (!posted.IsSuccess) return Result<PreOrderResponse>.Fail(posted.Error!.Code, posted.Error.Message);
        await store.Add(placed.Value!, ct);
        try { await store.Save(ct); }
        catch (ConcurrencyConflictException) { return Result<PreOrderResponse>.Fail("wallet.busy", "The wallet was being used at the same moment. Try again."); }
        return Result<PreOrderResponse>.Ok(PreOrderMapping.ToResponse(placed.Value!));
    }

    /// <summary>Cancelling before the cut-off gives back exactly what was taken, fee included.</summary>
    public async Task<Result<Unit>> Handle(CancelPreOrder c, CancellationToken ct)
    {
        var order = await store.GetPreOrder(c.PreOrderId, ct);
        if (order is null) return Result<Unit>.Fail("preorder.not_found", "Order not found.");
        if (await store.GuardianshipOf(c.CallerPersonId, order.HolderId, ct) is null) return Result<Unit>.Fail("permission.denied", "You are not linked to this child.");

        var now = clock.GetUtcNow();
        var cancelled = order.Cancel(now);
        if (!cancelled.IsSuccess) return Result<Unit>.Fail(cancelled.Error!.Code, cancelled.Error.Message);

        var purchase = (await store.GetPurchase(order.PurchaseId, ct))!;
        var legs = Postings.Reversal(order.WalletId, order.ShopId, purchase.AmountCents, purchase.FeeCents);
        var txn = LedgerTransaction.Create(LedgerTransactionType.PurchaseReversal, $"reversal:{order.Id:N}", legs, now, description: "Cancelled order refunded");
        if (!txn.IsSuccess) return Result<Unit>.Fail(txn.Error!.Code, txn.Error.Message);
        await store.Add(txn.Value!, ct);
        purchase.Reverse(now);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<List<PreOrderResponse>>> Handle(ListChildPreOrders q, CancellationToken ct)
    {
        if (await store.GuardianshipOf(q.CallerPersonId, q.HolderId, ct) is null) return Result<List<PreOrderResponse>>.Fail("permission.denied", "You are not linked to this child.");
        var from = BusinessDay.Of(clock.GetUtcNow()).AddDays(-7);
        return Result<List<PreOrderResponse>>.Ok((await store.PreOrdersOfHolder(q.HolderId, from, ct)).OrderBy(o => o.CollectOn).Select(PreOrderMapping.ToResponse).ToList());
    }
}

// ---------------------------------------------------------------- W4-3: the cashier hands them out

public sealed record ListPreOrdersForTill(Guid ShopId, Guid DeviceId, Guid StaffId, DateOnly? Day, string? BreakCode) : IQuery<List<TillPreOrder>>;
public sealed record CollectPreOrder(Guid ShopId, Guid PreOrderId, TillPreOrderCall Call) : ICommand<Unit>;

internal sealed class TillPreOrderHandlers(IWalletStore store, ITillAccessChecker tills, TimeProvider clock)
    : IQueryHandler<ListPreOrdersForTill, List<TillPreOrder>>, ICommandHandler<CollectPreOrder, Unit>
{
    public async Task<Result<List<TillPreOrder>>> Handle(ListPreOrdersForTill q, CancellationToken ct)
    {
        var access = await tills.Check(q.ShopId, q.DeviceId, q.StaffId, "sale.create", ct);
        if (!access.IsSuccess) return Result<List<TillPreOrder>>.Fail(access.Error!.Code, access.Error.Message);
        var school = await store.SchoolByShop(q.ShopId, ct);
        if (school is null) return Result<List<TillPreOrder>>.Fail("wallet.no_school", "This shop does not take wallet payments.");

        var day = q.Day ?? BusinessDay.Of(clock.GetUtcNow());
        var holders = (await store.HoldersOf(school.Id, ct)).ToDictionary(h => h.Id);
        var list = (await store.PreOrdersOf(school.Id, day, ct))
            .Where(o => o.Status != PreOrderStatus.Cancelled && (string.IsNullOrWhiteSpace(q.BreakCode) || o.BreakCode == q.BreakCode.Trim().ToLowerInvariant()))
            .Select(o => PreOrderMapping.ToTill(o, holders[o.HolderId]))
            .OrderBy(o => o.BreakCode).ThenBy(o => o.ClassName).ThenBy(o => o.HolderName).ToList();
        return Result<List<TillPreOrder>>.Ok(list);
    }

    public async Task<Result<Unit>> Handle(CollectPreOrder c, CancellationToken ct)
    {
        var access = await tills.Check(c.ShopId, c.Call.DeviceId, c.Call.StaffId, "sale.create", ct);
        if (!access.IsSuccess) return Result<Unit>.Fail(access.Error!.Code, access.Error.Message);
        var order = await store.GetPreOrder(c.PreOrderId, ct);
        var school = await store.SchoolByShop(c.ShopId, ct);
        if (order is null || school is null || order.SchoolId != school.Id) return Result<Unit>.Fail("preorder.not_found", "Order not found.");

        var collected = order.Collect();
        if (!collected.IsSuccess) return Result<Unit>.Fail(collected.Error!.Code, collected.Error.Message);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}
