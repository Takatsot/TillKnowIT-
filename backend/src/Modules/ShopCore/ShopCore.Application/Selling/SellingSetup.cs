using Identity.Contracts;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Selling;

// ---------------------------------------------------------------- B2-1: variants

public sealed record CreateVariants(Guid ShopId, Guid ProductId, CreateVariantsRequest Request) : ICommand<List<VariantResponse>>;
public sealed record ListVariants(Guid ShopId, Guid ProductId, Guid CallerPersonId) : IQuery<List<VariantResponse>>;
public sealed record SetSellingFlags(Guid ShopId, Guid ProductId, SellingFlagsRequest Request) : ICommand<Unit>;

internal sealed class VariantHandlers(ISellingStore store, IOrganisationAccess access, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<CreateVariants, List<VariantResponse>>, IQueryHandler<ListVariants, List<VariantResponse>>, ICommandHandler<SetSellingFlags, Unit>
{
    public const int MaxVariantsAtOnce = 200;

    public async Task<Result<List<VariantResponse>>> Handle(CreateVariants c, CancellationToken ct)
    {
        var allowed = await SellingAccess.PersonAndFeature(access, gate, c.Request.CallerPersonId, c.ShopId, "product.edit", "variants", ct);
        if (!allowed.IsSuccess) return Result<List<VariantResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var parent = await store.GetProduct(c.ShopId, c.ProductId, ct);
        if (parent is null) return Result<List<VariantResponse>>.Fail("product.not_found", "Product not found.");
        var asked = c.Request.Variants ?? [];
        if (asked.Count is 0 or > MaxVariantsAtOnce) return Result<List<VariantResponse>>.Fail("variant.count_invalid", $"Add from 1 to {MaxVariantsAtOnce} variants at a time.");

        var now = clock.GetUtcNow();
        var existingLabels = (await store.VariantsOf(parent.Id, ct)).Select(v => v.VariantLabel!.ToLowerInvariant()).ToHashSet();
        var barcodesInRequest = new HashSet<string>();
        var made = new List<Product>();
        foreach (var v in asked)
        {
            if (!existingLabels.Add((v.Label ?? "").Trim().ToLowerInvariant())) return Result<List<VariantResponse>>.Fail("variant.label_repeated", $"'{v.Label}' is already a variant of this product.");
            // Each variant has its own barcode; it may not clash with any product of the shop.
            var barcode = string.IsNullOrWhiteSpace(v.Barcode) ? null : v.Barcode.Trim();
            if (barcode is not null && (!barcodesInRequest.Add(barcode) || await store.BarcodeTaken(c.ShopId, barcode, ct)))
                return Result<List<VariantResponse>>.Fail("variant.barcode_taken", $"The barcode {barcode} is already used by another product.");

            var variant = Product.RecordVariant(parent, Guid.CreateVersion7(), c.Request.CallerPersonId, Guid.Empty, v.Label, barcode, v.CostCents, v.PriceCents, now);
            if (!variant.IsSuccess) return Result<List<VariantResponse>>.Fail(variant.Error!.Code, variant.Error.Message);
            made.Add(variant.Value!);
        }

        foreach (var m in made) store.Add(m);
        await store.Save(ct);
        return Result<List<VariantResponse>>.Ok(made.Select(ToResponse).ToList());
    }

    public async Task<Result<List<VariantResponse>>> Handle(ListVariants q, CancellationToken ct)
    {
        var allowed = await SellingAccess.Person(access, q.CallerPersonId, q.ShopId, "product.edit", ct);
        if (!allowed.IsSuccess) return Result<List<VariantResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var parent = await store.GetProduct(q.ShopId, q.ProductId, ct);
        if (parent is null) return Result<List<VariantResponse>>.Fail("product.not_found", "Product not found.");
        return Result<List<VariantResponse>>.Ok((await store.VariantsOf(parent.Id, ct)).OrderBy(v => v.VariantLabel).Select(ToResponse).ToList());
    }

    public async Task<Result<Unit>> Handle(SetSellingFlags c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await SellingAccess.Person(access, r.CallerPersonId, c.ShopId, "product.edit", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var product = await store.GetProduct(c.ShopId, c.ProductId, ct);
        if (product is null) return Result<Unit>.Fail("product.not_found", "Product not found.");

        // A flag can only be turned on in a shop whose pack has the feature; turning one off is always allowed.
        if (r.SoldByWeight && !product.SoldByWeight && !await gate.IsOn(c.ShopId, "weight", ct)) return Result<Unit>.Fail("feature.not_enabled", "'weight' is not switched on for this shop.");
        if (r.IsAddOn && !product.IsAddOn && !await gate.IsOn(c.ShopId, "modifiers", ct)) return Result<Unit>.Fail("feature.not_enabled", "'modifiers' is not switched on for this shop.");
        if (r.RequiresSerial && !product.RequiresSerial && !await gate.IsOn(c.ShopId, "serial_numbers", ct)) return Result<Unit>.Fail("feature.not_enabled", "'serial_numbers' is not switched on for this shop.");
        if (r.SoldByWeight && r.IsAddOn) return Result<Unit>.Fail("product.flags_invalid", "An add-on cannot be sold by weight.");

        product.SetSellingFlags(r.SoldByWeight, r.IsAddOn, r.RequiresSerial);
        // Variants carry their parent's way of being sold.
        foreach (var v in await store.VariantsOf(product.Id, ct)) v.SetSellingFlags(r.SoldByWeight, v.IsAddOn, r.RequiresSerial);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    private static VariantResponse ToResponse(Product v) => new(v.Id, v.ParentProductId!.Value, v.Name, v.VariantLabel!, v.Barcode, v.CostCents, v.PriceCents);
}

// ---------------------------------------------------------------- B2-2: modifiers

public sealed record SaveModifierGroup(Guid ShopId, Guid? GroupId, ModifierGroupRequest Request) : ICommand<ModifierGroupResponse>;
public sealed record ListModifierGroups(Guid ShopId, Guid DeviceId, Guid StaffId) : IQuery<List<ModifierGroupResponse>>;
public sealed record AssignModifierGroups(Guid ShopId, Guid ProductId, AssignModifierGroupsRequest Request) : ICommand<Unit>;
public sealed record GetProductModifiers(Guid ShopId, Guid ProductId, Guid DeviceId, Guid StaffId) : IQuery<List<ModifierGroupResponse>>;
public sealed record ValidateModifiers(Guid ShopId, Guid ProductId, ValidateModifiersRequest Request) : IQuery<long>;

internal sealed class ModifierHandlers(ISellingStore store, IOrganisationAccess access, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<SaveModifierGroup, ModifierGroupResponse>, IQueryHandler<ListModifierGroups, List<ModifierGroupResponse>>,
      ICommandHandler<AssignModifierGroups, Unit>, IQueryHandler<GetProductModifiers, List<ModifierGroupResponse>>, IQueryHandler<ValidateModifiers, long>
{
    public const int MaxGroupsPerProduct = 10;

    public async Task<Result<ModifierGroupResponse>> Handle(SaveModifierGroup c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await SellingAccess.PersonAndFeature(access, gate, r.CallerPersonId, c.ShopId, "product.edit", "modifiers", ct);
        if (!allowed.IsSuccess) return Result<ModifierGroupResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        // Every option is a product of this shop.
        var options = (r.OptionProductIds ?? []).ToList();
        var products = await store.ProductsByIds(c.ShopId, options, ct);
        if (products.Count != options.Distinct().Count()) return Result<ModifierGroupResponse>.Fail("modifier.option_unknown", "One of the options is not a product of this shop.");

        ModifierGroup group;
        if (c.GroupId is { } id)
        {
            var existing = await store.GetGroup(c.ShopId, id, ct);
            if (existing is null) return Result<ModifierGroupResponse>.Fail("modifier.not_found", "Group not found.");
            var changed = existing.Change(r.Name, r.Required, r.Min, r.Max, options);
            if (!changed.IsSuccess) return Result<ModifierGroupResponse>.Fail(changed.Error!.Code, changed.Error.Message);
            group = existing;
        }
        else
        {
            var created = ModifierGroup.Create(c.ShopId, r.Name, r.Required, r.Min, r.Max, options, clock.GetUtcNow());
            if (!created.IsSuccess) return Result<ModifierGroupResponse>.Fail(created.Error!.Code, created.Error.Message);
            group = created.Value!;
            store.Add(group);
        }
        await store.Save(ct);
        return Result<ModifierGroupResponse>.Ok(ToResponse(group, products.ToDictionary(p => p.Id)));
    }

    public async Task<Result<List<ModifierGroupResponse>>> Handle(ListModifierGroups q, CancellationToken ct)
    {
        var allowed = await SellingAccess.TillAndFeature(tills, gate, q.ShopId, q.DeviceId, q.StaffId, "sale.create", "modifiers", ct);
        if (!allowed.IsSuccess) return Result<List<ModifierGroupResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<List<ModifierGroupResponse>>.Ok(await Respond(q.ShopId, await store.Groups(q.ShopId, ct), ct));
    }

    public async Task<Result<Unit>> Handle(AssignModifierGroups c, CancellationToken ct)
    {
        var allowed = await SellingAccess.PersonAndFeature(access, gate, c.Request.CallerPersonId, c.ShopId, "product.edit", "modifiers", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (await store.GetProduct(c.ShopId, c.ProductId, ct) is null) return Result<Unit>.Fail("product.not_found", "Product not found.");
        var ids = (c.Request.GroupIds ?? []).Distinct().ToList();
        if (ids.Count > MaxGroupsPerProduct) return Result<Unit>.Fail("modifier.too_many_groups", $"A product has at most {MaxGroupsPerProduct} groups of add-ons.");
        if ((await store.GroupsByIds(c.ShopId, ids, ct)).Count != ids.Count) return Result<Unit>.Fail("modifier.not_found", "One of the groups does not exist.");

        await store.ReplaceProductGroups(c.ProductId, ids, ct);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<List<ModifierGroupResponse>>> Handle(GetProductModifiers q, CancellationToken ct)
    {
        var allowed = await SellingAccess.TillAndFeature(tills, gate, q.ShopId, q.DeviceId, q.StaffId, "sale.create", "modifiers", ct);
        if (!allowed.IsSuccess) return Result<List<ModifierGroupResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var ids = await store.GroupIdsOfProduct(q.ProductId, ct);
        return Result<List<ModifierGroupResponse>>.Ok(await Respond(q.ShopId, await store.GroupsByIds(q.ShopId, ids, ct), ct));
    }

    /// <summary>What the chosen add-ons add to the price, after checking them against the product's groups. Online; the till does the same sum locally.</summary>
    public async Task<Result<long>> Handle(ValidateModifiers q, CancellationToken ct)
    {
        var allowed = await SellingAccess.TillAndFeature(tills, gate, q.ShopId, q.Request.DeviceId, q.Request.StaffId, "sale.create", "modifiers", ct);
        if (!allowed.IsSuccess) return Result<long>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var groups = await store.GroupsByIds(q.ShopId, await store.GroupIdsOfProduct(q.ProductId, ct), ct);
        var prices = (await store.ProductsByIds(q.ShopId, groups.SelectMany(g => g.Options.Select(o => o.ProductId)).Distinct().ToList(), ct)).ToDictionary(p => p.Id);

        var defs = groups.Select(g => new ModifierGroupDef(g.Id, g.Name, g.Required, g.Min, g.Max,
            g.Options.OrderBy(o => o.SortOrder).Select(o => new ModifierOption(o.ProductId, prices[o.ProductId].Name, prices[o.ProductId].PriceCents)).ToList())).ToList();
        return ModifierSelection.Price(defs, q.Request.ChosenProductIds ?? []);
    }

    private async Task<List<ModifierGroupResponse>> Respond(Guid shopId, List<ModifierGroup> groups, CancellationToken ct)
    {
        var products = (await store.ProductsByIds(shopId, groups.SelectMany(g => g.Options.Select(o => o.ProductId)).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        return groups.OrderBy(g => g.Name).Select(g => ToResponse(g, products)).ToList();
    }

    private static ModifierGroupResponse ToResponse(ModifierGroup g, IReadOnlyDictionary<Guid, Product> products) => new(
        g.Id, g.Name, g.Required, g.Min, g.Max,
        g.Options.OrderBy(o => o.SortOrder).Select(o => new ModifierOptionResponse(o.ProductId, products[o.ProductId].Name, products[o.ProductId].PriceCents)).ToList());
}

// ---------------------------------------------------------------- B2-3: promotions

public sealed record SavePromotion(Guid ShopId, Guid? PromotionId, PromotionRequest Request) : ICommand<PromotionResponse>;
public sealed record SetPromotionActive(Guid ShopId, Guid PromotionId, SetActiveRequest Request) : ICommand<PromotionResponse>;
public sealed record ListPromotions(Guid ShopId, Guid DeviceId, Guid StaffId) : IQuery<List<PromotionResponse>>;
public sealed record PriceBasket(Guid ShopId, PriceBasketRequest Request) : IQuery<PricedBasketResponse>;

internal static class PromotionMapping
{
    private static readonly (Weekdays Day, string Code)[] Days =
        [(Weekdays.Mon, "mon"), (Weekdays.Tue, "tue"), (Weekdays.Wed, "wed"), (Weekdays.Thu, "thu"), (Weekdays.Fri, "fri"), (Weekdays.Sat, "sat"), (Weekdays.Sun, "sun")];

    public static Result<Weekdays> ParseDays(List<string>? days)
    {
        if (days is null or { Count: 0 }) return Result<Weekdays>.Ok(Weekdays.All);
        var set = Weekdays.None;
        foreach (var d in days)
        {
            var match = Days.FirstOrDefault(x => x.Code == d?.Trim().ToLowerInvariant());
            if (match.Code is null) return Result<Weekdays>.Fail("promotion.days_invalid", "Days are mon, tue, wed, thu, fri, sat and sun.");
            set |= match.Day;
        }
        return Result<Weekdays>.Ok(set);
    }

    public static bool TryParseType(string? text, out PromotionType type)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "percent_off": type = PromotionType.PercentOff; return true;
            case "amount_off": type = PromotionType.AmountOff; return true;
            case "fixed_price": type = PromotionType.FixedPrice; return true;
            case "combo": type = PromotionType.Combo; return true;
            default: type = default; return false;
        }
    }

    public static string TypeCode(PromotionType t) => t switch
    {
        PromotionType.PercentOff => "percent_off", PromotionType.AmountOff => "amount_off", PromotionType.FixedPrice => "fixed_price", _ => "combo",
    };

    public static Result<TimeOnly?> ParseHour(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Result<TimeOnly?>.Ok(null);
        return TimeOnly.TryParseExact(text.Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t)
            ? Result<TimeOnly?>.Ok(t)
            : Result<TimeOnly?>.Fail("promotion.hours_invalid", "Hours are written like 16:00.");
    }

    public static PromotionResponse ToResponse(Promotion p)
    {
        var def = p.ToDef();
        return new PromotionResponse(p.Id, p.Name, TypeCode(p.Type), def.ProductIds.ToList(), p.Percent, p.AmountCents, def.Items.Select(i => new ComboItemDto(i.ProductId, i.Quantity)).ToList(),
            p.StartsAt, p.EndsAt, Days.Where(d => (p.Days & d.Day) != 0).Select(d => d.Code).ToList(), p.FromTime?.ToString("HH:mm"), p.ToTime?.ToString("HH:mm"), p.IsActive);
    }
}

internal sealed class PromotionHandlers(ISellingStore store, IOrganisationAccess access, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<SavePromotion, PromotionResponse>, ICommandHandler<SetPromotionActive, PromotionResponse>,
      IQueryHandler<ListPromotions, List<PromotionResponse>>, IQueryHandler<PriceBasket, PricedBasketResponse>
{
    public async Task<Result<PromotionResponse>> Handle(SavePromotion c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await SellingAccess.PersonAndFeature(access, gate, r.CallerPersonId, c.ShopId, "shop.settings", "promotions", ct);
        if (!allowed.IsSuccess) return Result<PromotionResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (!PromotionMapping.TryParseType(r.Type, out var type)) return Result<PromotionResponse>.Fail("promotion.type_invalid", "Type is percent_off, amount_off, fixed_price or combo.");
        var days = PromotionMapping.ParseDays(r.Days);
        if (!days.IsSuccess) return Result<PromotionResponse>.Fail(days.Error!.Code, days.Error.Message);
        var from = PromotionMapping.ParseHour(r.From);
        var to = PromotionMapping.ParseHour(r.To);
        if (!from.IsSuccess) return Result<PromotionResponse>.Fail(from.Error!.Code, from.Error.Message);
        if (!to.IsSuccess) return Result<PromotionResponse>.Fail(to.Error!.Code, to.Error.Message);

        var products = (r.ProductIds ?? []).Distinct().ToList();
        var items = (r.Items ?? []).Select(i => new ComboItem(i.ProductId, i.Quantity)).ToList();
        var named = products.Concat(items.Select(i => i.ProductId)).Distinct().ToList();
        if (named.Count > 0 && (await store.ProductsByIds(c.ShopId, named, ct)).Count != named.Count)
            return Result<PromotionResponse>.Fail("promotion.product_unknown", "A product in the promotion is not a product of this shop.");

        var input = new Promotion.Input(r.Name, type, products, r.Percent, r.AmountCents, items, r.StartsAt, r.EndsAt, days.Value, from.Value, to.Value);
        Promotion promotion;
        if (c.PromotionId is { } id)
        {
            var existing = await store.GetPromotion(c.ShopId, id, ct);
            if (existing is null) return Result<PromotionResponse>.Fail("promotion.not_found", "Promotion not found.");
            var changed = existing.Change(input);
            if (!changed.IsSuccess) return Result<PromotionResponse>.Fail(changed.Error!.Code, changed.Error.Message);
            promotion = existing;
        }
        else
        {
            var created = Promotion.Create(c.ShopId, input, clock.GetUtcNow());
            if (!created.IsSuccess) return Result<PromotionResponse>.Fail(created.Error!.Code, created.Error.Message);
            promotion = created.Value!;
            store.Add(promotion);
        }
        await store.Save(ct);
        return Result<PromotionResponse>.Ok(PromotionMapping.ToResponse(promotion));
    }

    public async Task<Result<PromotionResponse>> Handle(SetPromotionActive c, CancellationToken ct)
    {
        var allowed = await SellingAccess.PersonAndFeature(access, gate, c.Request.CallerPersonId, c.ShopId, "shop.settings", "promotions", ct);
        if (!allowed.IsSuccess) return Result<PromotionResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var promotion = await store.GetPromotion(c.ShopId, c.PromotionId, ct);
        if (promotion is null) return Result<PromotionResponse>.Fail("promotion.not_found", "Promotion not found.");
        promotion.SetActive(c.Request.Active);
        await store.Save(ct);
        return Result<PromotionResponse>.Ok(PromotionMapping.ToResponse(promotion));
    }

    /// <summary>What a till downloads: the shop's promotions, to apply them at the counter without a connection.</summary>
    public async Task<Result<List<PromotionResponse>>> Handle(ListPromotions q, CancellationToken ct)
    {
        var allowed = await SellingAccess.TillAndFeature(tills, gate, q.ShopId, q.DeviceId, q.StaffId, "sale.create", "promotions", ct);
        if (!allowed.IsSuccess) return Result<List<PromotionResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<List<PromotionResponse>>.Ok((await store.Promotions(q.ShopId, ct)).OrderBy(p => p.Name).Select(PromotionMapping.ToResponse).ToList());
    }

    /// <summary>The same sum the till does, run on the server: a till that is online can ask instead of working it out, and a back office can preview a basket.</summary>
    public async Task<Result<PricedBasketResponse>> Handle(PriceBasket q, CancellationToken ct)
    {
        var r = q.Request;
        var allowed = await SellingAccess.Till(tills, q.ShopId, r.DeviceId, r.StaffId, "sale.create", ct);
        if (!allowed.IsSuccess) return Result<PricedBasketResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (r.Lines is null or { Count: 0 } or { Count: > 200 } || r.Lines.Any(l => l.Quantity <= 0 || l.UnitPriceCents < 0)) return Result<PricedBasketResponse>.Fail("basket.invalid", "A basket needs from 1 to 200 items with a quantity and a price.");

        var promotions = await gate.IsOn(q.ShopId, "promotions", ct) ? (await store.Promotions(q.ShopId, ct)).Select(p => p.ToDef()).ToList() : [];
        var priced = PromotionEngine.Price(r.Lines.Select(l => new BasketLine(l.ProductId, l.Quantity, l.UnitPriceCents)).ToList(), promotions, r.At ?? clock.GetUtcNow());
        return Result<PricedBasketResponse>.Ok(new PricedBasketResponse(priced.SubtotalCents, priced.DiscountCents, priced.TotalCents,
            priced.DiscountsByProduct.ToDictionary(x => x.Key, x => x.Value), priced.Applied.Select(a => new AppliedPromotionDto(a.PromotionId, a.Name, a.DiscountCents, a.Times)).ToList()));
    }
}

// ---------------------------------------------------------------- B3-1: the floor plan

public sealed record SaveFloorPlan(Guid ShopId, FloorPlanRequest Request) : ICommand<List<FloorTableResponse>>;
public sealed record GetFloorPlan(Guid ShopId, Guid DeviceId, Guid StaffId) : IQuery<List<FloorTableResponse>>;

internal sealed class FloorPlanHandlers(ISellingStore store, IOrganisationAccess access, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<SaveFloorPlan, List<FloorTableResponse>>, IQueryHandler<GetFloorPlan, List<FloorTableResponse>>
{
    /// <summary>
    /// Replaces the plan. A table that is in the new plan keeps its id (matched by area and name), so tickets that point at it still do; a table that is
    /// gone is retired, never deleted.
    /// </summary>
    public async Task<Result<List<FloorTableResponse>>> Handle(SaveFloorPlan c, CancellationToken ct)
    {
        var allowed = await SellingAccess.PersonAndFeature(access, gate, c.Request.CallerPersonId, c.ShopId, "shop.settings", "tables", ct);
        if (!allowed.IsSuccess) return Result<List<FloorTableResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var plan = (c.Request.Tables ?? []).Select(t => new FloorTable.Input(t.Area?.Trim() ?? "", t.Name?.Trim() ?? "", t.Seats)).ToList();
        var valid = FloorTable.Validate(plan);
        if (!valid.IsSuccess) return Result<List<FloorTableResponse>>.Fail(valid.Error!.Code, valid.Error.Message);

        var existing = await store.Tables(c.ShopId, ct);
        string Key(string area, string name) => $"{area.ToLowerInvariant()}|{name.ToLowerInvariant()}";
        var byKey = existing.ToDictionary(t => Key(t.Area, t.Name));
        var keep = new HashSet<string>();
        var now = clock.GetUtcNow();
        for (var i = 0; i < plan.Count; i++)
        {
            var key = Key(plan[i].Area, plan[i].Name);
            keep.Add(key);
            if (byKey.TryGetValue(key, out var table)) table.Update(plan[i].Seats, i);
            else store.Add(new FloorTable(c.ShopId, plan[i].Area, plan[i].Name, plan[i].Seats, i, now));
        }
        foreach (var gone in existing.Where(t => !keep.Contains(Key(t.Area, t.Name)))) gone.Retire();

        await store.Save(ct);
        return Result<List<FloorTableResponse>>.Ok((await store.Tables(c.ShopId, ct)).Where(t => t.IsActive).OrderBy(t => t.SortOrder).Select(t => new FloorTableResponse(t.Id, t.Area, t.Name, t.Seats)).ToList());
    }

    public async Task<Result<List<FloorTableResponse>>> Handle(GetFloorPlan q, CancellationToken ct)
    {
        var allowed = await SellingAccess.TillAndFeature(tills, gate, q.ShopId, q.DeviceId, q.StaffId, "sale.create", "tables", ct);
        if (!allowed.IsSuccess) return Result<List<FloorTableResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<List<FloorTableResponse>>.Ok((await store.Tables(q.ShopId, ct)).Where(t => t.IsActive).OrderBy(t => t.SortOrder).Select(t => new FloorTableResponse(t.Id, t.Area, t.Name, t.Seats)).ToList());
    }
}
