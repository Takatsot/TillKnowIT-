using TillKnowIT.SharedKernel;

namespace ShopCore.Domain.Selling;

// ---------------------------------------------------------------- B2-2: modifiers and add-ons

public sealed record ModifierOption(Guid Id, string Name, long PriceCents);

/// <summary>A group of add-ons on a product, such as "Sauce" or "Extras". Required groups need at least one choice; Max is the most that may be chosen.</summary>
public sealed record ModifierGroupDef(Guid Id, string Name, bool Required, int Min, int Max, IReadOnlyList<ModifierOption> Options)
{
    public static Result Validate(string name, bool required, int min, int max, int options)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 60) return Result.Fail("modifier.name_invalid", "Give the group a name of up to 60 characters.");
        if (min < 0 || max < 1 || max > 20 || min > max) return Result.Fail("modifier.limits_invalid", "Choose a minimum of 0 or more and a maximum of 1 to 20, with the minimum no higher than the maximum.");
        if (required && min < 1) return Result.Fail("modifier.limits_invalid", "A required group needs a minimum of at least 1.");
        if (options < 1 || options > 50) return Result.Fail("modifier.options_invalid", "A group needs from 1 to 50 options.");
        return Result.Ok();
    }
}

public static class ModifierSelection
{
    /// <summary>
    /// Checks what the cashier chose against the groups on the product and returns the extra price. Every group is checked, not just those chosen from:
    /// a required group left empty fails. An option that is not in any of the product's groups is refused.
    /// </summary>
    public static Result<long> Price(IReadOnlyList<ModifierGroupDef> groups, IReadOnlyCollection<Guid> chosen)
    {
        if (chosen.Count != chosen.Distinct().Count()) return Result<long>.Fail("modifier.repeated", "Each option can be chosen once.");
        var known = groups.SelectMany(g => g.Options).ToDictionary(o => o.Id);
        var stranger = chosen.FirstOrDefault(id => !known.ContainsKey(id));
        if (stranger != Guid.Empty) return Result<long>.Fail("modifier.unknown", "One of the choices does not belong to this item.");

        long extra = 0;
        foreach (var g in groups)
        {
            var picked = g.Options.Count(o => chosen.Contains(o.Id));
            if (picked > g.Max) return Result<long>.Fail("modifier.too_many", $"{g.Name}: choose at most {g.Max}.");
            if (picked < g.Min || (g.Required && picked == 0)) return Result<long>.Fail("modifier.too_few", $"{g.Name}: choose at least {Math.Max(1, g.Min)}.");
            extra += g.Options.Where(o => chosen.Contains(o.Id)).Sum(o => o.PriceCents);
        }
        return Result<long>.Ok(extra);
    }
}

// ---------------------------------------------------------------- B2-3: promotions and combos

public enum PromotionType { PercentOff, AmountOff, FixedPrice, Combo }

[Flags]
public enum Weekdays { None = 0, Mon = 1, Tue = 2, Wed = 4, Thu = 8, Fri = 16, Sat = 32, Sun = 64, All = 127 }

/// <summary>One item of a combo: how many of this product it takes.</summary>
public sealed record ComboItem(Guid ProductId, int Quantity);

/// <summary>
/// A promotion as the pricing engine sees it. PercentOff takes Percent off; AmountOff takes AmountCents off each unit; FixedPrice sells each unit at
/// AmountCents; Combo sells the whole set of Items for AmountCents. ProductIds empty means every product (percent promotions only).
/// The schedule is in South African time: the days it runs and, if set, the hours (From inclusive, To exclusive).
/// </summary>
public sealed record PromotionDef(
    Guid Id, string Name, PromotionType Type, IReadOnlyList<Guid> ProductIds, int Percent, long AmountCents, IReadOnlyList<ComboItem> Items,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, Weekdays Days, TimeOnly? From, TimeOnly? To, bool IsActive = true)
{
    public bool IsRunning(DateTimeOffset now)
    {
        if (!IsActive || now < StartsAt || now >= EndsAt) return false;
        var local = now.ToOffset(BusinessDay.Offset);
        var day = local.DayOfWeek switch
        {
            DayOfWeek.Monday => Weekdays.Mon, DayOfWeek.Tuesday => Weekdays.Tue, DayOfWeek.Wednesday => Weekdays.Wed, DayOfWeek.Thursday => Weekdays.Thu,
            DayOfWeek.Friday => Weekdays.Fri, DayOfWeek.Saturday => Weekdays.Sat, _ => Weekdays.Sun,
        };
        if ((Days & day) == 0) return false;
        if (From is { } from && To is { } to)
        {
            var t = TimeOnly.FromTimeSpan(local.TimeOfDay);
            // A window that runs past midnight (22:00 to 02:00) counts the small hours as part of the evening before.
            return from <= to ? t >= from && t < to : t >= from || t < to;
        }
        return true;
    }

    public static Result Validate(PromotionType type, string name, IReadOnlyList<Guid> productIds, int percent, long amountCents, IReadOnlyList<ComboItem> items, DateTimeOffset startsAt, DateTimeOffset endsAt, Weekdays days)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) return Result.Fail("promotion.name_invalid", "Give the promotion a name of up to 80 characters.");
        if (endsAt <= startsAt) return Result.Fail("promotion.dates_invalid", "The promotion must end after it starts.");
        if (days == Weekdays.None) return Result.Fail("promotion.days_invalid", "Choose at least one day.");
        switch (type)
        {
            case PromotionType.PercentOff:
                if (percent is < 1 or > 100) return Result.Fail("promotion.percent_invalid", "A percentage is from 1 to 100.");
                break;
            case PromotionType.AmountOff or PromotionType.FixedPrice:
                if (amountCents < 0 || (type == PromotionType.AmountOff && amountCents == 0)) return Result.Fail("promotion.amount_invalid", "The amount must be more than zero.");
                if (productIds.Count == 0) return Result.Fail("promotion.products_required", "Choose which products it applies to.");
                break;
            case PromotionType.Combo:
                if (items.Count < 2) return Result.Fail("promotion.combo_items", "A combo needs at least two items.");
                if (items.Any(i => i.Quantity < 1 || i.Quantity > 20) || items.Select(i => i.ProductId).Distinct().Count() != items.Count) return Result.Fail("promotion.combo_items", "Each product appears once in a combo, with a quantity of 1 to 20.");
                if (amountCents <= 0) return Result.Fail("promotion.amount_invalid", "The combo price must be more than zero.");
                break;
        }
        return Result.Ok();
    }
}

/// <summary>A line in the basket as the pricing engine sees it. Quantity is units; the price is what one unit costs before any promotion.</summary>
public sealed record BasketLine(Guid ProductId, int Quantity, long UnitPriceCents);

public sealed record AppliedPromotion(Guid PromotionId, string Name, long DiscountCents, int Times);

/// <summary>DiscountsByProduct is the total taken off each product's line. Nothing is ever taken off more than the line costs.</summary>
public sealed record PricedBasket(long SubtotalCents, long DiscountCents, long TotalCents, IReadOnlyDictionary<Guid, long> DiscountsByProduct, IReadOnlyList<AppliedPromotion> Applied);

/// <summary>
/// The pricing strategy for promotions. It is given the basket and the promotions and knows nothing about which business pack the shop is on:
/// a shop with no promotions simply gets back the basket unchanged. Promotions do not stack: each unit is discounted by at most one promotion,
/// the one that saves the customer most, and combos are fitted first because they save the most per item.
/// </summary>
public static class PromotionEngine
{
    public static PricedBasket Price(IReadOnlyList<BasketLine> basket, IReadOnlyList<PromotionDef> promotions, DateTimeOffset now)
    {
        var subtotal = basket.Sum(l => l.UnitPriceCents * l.Quantity);
        var running = promotions.Where(p => p.IsRunning(now)).ToList();
        if (running.Count == 0 || basket.Count == 0) return new PricedBasket(subtotal, 0, subtotal, new Dictionary<Guid, long>(), []);

        // Units still free to be discounted, per product. A unit that went into a combo is used up.
        var free = basket.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var price = basket.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.First().UnitPriceCents);
        var discounts = new Dictionary<Guid, long>();
        var applied = new List<AppliedPromotion>();

        void Add(Guid product, long cents) => discounts[product] = discounts.GetValueOrDefault(product) + cents;

        // 1. Combos, the biggest saving per application first.
        var combos = running.Where(p => p.Type == PromotionType.Combo).Where(p => p.Items.All(i => price.ContainsKey(i.ProductId))).ToList();
        while (true)
        {
            PromotionDef? best = null;
            long bestSaving = 0;
            foreach (var combo in combos.OrderBy(c => c.Id))
            {
                if (combo.Items.Any(i => free.GetValueOrDefault(i.ProductId) < i.Quantity)) continue;
                var saving = combo.Items.Sum(i => price[i.ProductId] * i.Quantity) - combo.AmountCents;
                if (saving > bestSaving) { best = combo; bestSaving = saving; }
            }
            if (best is null) break;

            // Share the saving across the items in proportion to what each would have cost, so the per-line discounts add up to exactly the saving.
            var weights = best.Items.Select(i => (i.ProductId, Cost: price[i.ProductId] * i.Quantity)).ToList();
            long given = 0;
            for (var k = 0; k < weights.Count; k++)
            {
                var share = k == weights.Count - 1 ? bestSaving - given : bestSaving * weights[k].Cost / weights.Sum(w => w.Cost);
                given += share;
                Add(weights[k].ProductId, share);
            }
            foreach (var i in best.Items) free[i.ProductId] -= i.Quantity;
            var existing = applied.FindIndex(a => a.PromotionId == best.Id);
            if (existing >= 0) applied[existing] = applied[existing] with { DiscountCents = applied[existing].DiscountCents + bestSaving, Times = applied[existing].Times + 1 };
            else applied.Add(new AppliedPromotion(best.Id, best.Name, bestSaving, 1));
        }

        // 2. Per-unit promotions on whatever units are left: the best one for each product.
        foreach (var (product, units) in free.Where(f => f.Value > 0).OrderBy(f => f.Key))
        {
            var unit = price[product];
            PromotionDef? winner = null;
            long perUnit = 0;
            foreach (var p in running.Where(p => p.Type != PromotionType.Combo).OrderBy(p => p.Id))
            {
                if (p.ProductIds.Count > 0 && !p.ProductIds.Contains(product)) continue;
                var off = p.Type switch
                {
                    PromotionType.PercentOff => (unit * p.Percent + 50) / 100,
                    PromotionType.AmountOff => Math.Min(unit, p.AmountCents),
                    PromotionType.FixedPrice => Math.Max(0, unit - p.AmountCents),
                    _ => 0,
                };
                if (off > perUnit) { winner = p; perUnit = off; }
            }
            if (winner is null) continue;
            var total = perUnit * units;
            Add(product, total);
            var existing = applied.FindIndex(a => a.PromotionId == winner.Id);
            if (existing >= 0) applied[existing] = applied[existing] with { DiscountCents = applied[existing].DiscountCents + total, Times = applied[existing].Times + units };
            else applied.Add(new AppliedPromotion(winner.Id, winner.Name, total, units));
        }

        var discount = discounts.Values.Sum();
        return new PricedBasket(subtotal, discount, subtotal - discount, discounts, applied);
    }
}
