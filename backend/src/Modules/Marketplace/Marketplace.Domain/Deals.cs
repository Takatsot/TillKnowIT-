using System.Text.Json;
using TillKnowIT.SharedKernel;

namespace Marketplace.Domain;

public enum DealType { Discount, FreeGoods, Combo }

public sealed record ComboItem(Guid SupplierProductId, int Cases);

/// <summary>
/// The terms of a deal. Which fields apply depends on the type:
/// Discount: PercentOff (1 to 90) or CentsOffPerCase, on one product.
/// FreeGoods: BuyCases and FreeCases ("buy 10 cases, get 1 free"), on one product.
/// Combo: Items and ComboPriceCents for the whole set.
/// </summary>
public sealed record DealTerms(
    Guid? SupplierProductId = null, int? PercentOff = null, long? CentsOffPerCase = null, int? BuyCases = null, int? FreeCases = null,
    List<ComboItem>? Items = null, long? ComboPriceCents = null);

public sealed record DealInput(
    string Title, DealType Type, DealTerms Terms, DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyCollection<string> AreaCodes, bool IsSponsored);

/// <summary>M2-3: an offer a supplier makes to shops for a limited time. Sponsored deals are labelled as such wherever they are shown.</summary>
public sealed class Deal : VersionedEntity
{
    public const int MaxDays = 180;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Deal() { }

    /// <param name="supplierAreaCodes">The areas the supplier delivers to. A deal can only target areas the supplier serves.</param>
    public static Result<Deal> Create(Guid supplierId, DealInput input, IReadOnlyCollection<string> supplierAreaCodes, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 120)
            return Result<Deal>.Fail("deal.title_invalid", "A deal needs a title (up to 120 characters).");
        if (input.EndsAt <= input.StartsAt)
            return Result<Deal>.Fail("deal.dates_invalid", "A deal must end after it starts.");
        if (input.EndsAt <= now)
            return Result<Deal>.Fail("deal.already_over", "That deal has already ended.");
        if ((input.EndsAt - input.StartsAt).TotalDays > MaxDays)
            return Result<Deal>.Fail("deal.too_long", $"A deal can run for at most {MaxDays} days.");
        if (input.AreaCodes.Count == 0)
            return Result<Deal>.Fail("deal.areas_required", "Choose the areas the deal is for.");
        if (input.AreaCodes.Any(a => !supplierAreaCodes.Contains(a)))
            return Result<Deal>.Fail("deal.area_not_served", "A deal can only be for areas you deliver to.");

        var terms = CheckTerms(input.Type, input.Terms);
        if (!terms.IsSuccess) return Result<Deal>.Fail(terms.Error!.Code, terms.Error.Message);

        var deal = new Deal
        {
            SupplierId = supplierId, Title = input.Title.Trim(), Type = input.Type, TermsJson = JsonSerializer.Serialize(input.Terms, Json),
            StartsAt = input.StartsAt, EndsAt = input.EndsAt, AreaCodes = string.Join(',', input.AreaCodes.Distinct().Order()), IsSponsored = input.IsSponsored,
        };
        deal.StampFromDevice(Guid.CreateVersion7(), now, null, null);
        return Result<Deal>.Ok(deal);
    }

    private static Result CheckTerms(DealType type, DealTerms t)
    {
        switch (type)
        {
            case DealType.Discount:
                if (t.SupplierProductId is null) return Result.Fail("deal.product_required", "Choose the product the discount is for.");
                if ((t.PercentOff is null) == (t.CentsOffPerCase is null)) return Result.Fail("deal.discount_invalid", "Give either a percentage or an amount off per case, not both.");
                if (t.PercentOff is < 1 or > 90) return Result.Fail("deal.discount_invalid", "A percentage discount is between 1 and 90.");
                if (t.CentsOffPerCase is <= 0) return Result.Fail("deal.discount_invalid", "The amount off must be more than zero.");
                return Result.Ok();
            case DealType.FreeGoods:
                if (t.SupplierProductId is null) return Result.Fail("deal.product_required", "Choose the product the deal is for.");
                if (t.BuyCases is not > 0 || t.FreeCases is not > 0) return Result.Fail("deal.free_goods_invalid", "Say how many cases to buy and how many come free.");
                return Result.Ok();
            default:
                if (t.Items is null || t.Items.Count < 2) return Result.Fail("deal.combo_invalid", "A combo needs at least two products.");
                if (t.Items.Any(i => i.Cases < 1) || t.Items.Select(i => i.SupplierProductId).Distinct().Count() != t.Items.Count)
                    return Result.Fail("deal.combo_invalid", "Each product in a combo appears once, with at least one case.");
                if (t.ComboPriceCents is not > 0) return Result.Fail("deal.combo_invalid", "Give the price for the whole combo.");
                return Result.Ok();
        }
    }

    public Result Cancel()
    {
        if (IsCancelled) return Result.Ok();
        IsCancelled = true;
        Touch();
        return Result.Ok();
    }

    public DealTerms Terms => JsonSerializer.Deserialize<DealTerms>(TermsJson, Json)!;

    public bool IsActiveAt(DateTimeOffset now) => !IsCancelled && now >= StartsAt && now < EndsAt;

    public bool AppliesToArea(string municipalityCode) => AreaList.Contains(municipalityCode);

    public IReadOnlyList<string> AreaList => AreaCodes.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public Guid SupplierId { get; private set; }
    public string Title { get; private set; } = default!;
    public DealType Type { get; private set; }
    public string TermsJson { get; private set; } = default!;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string AreaCodes { get; private set; } = default!;
    public bool IsSponsored { get; private set; }
    public bool IsCancelled { get; private set; }
}

/// <summary>What a deal does to the price of buying some cases of one product.</summary>
public sealed record DealEffect(long TotalCostCents, int CasesReceived);

public static class DealCalculator
{
    /// <summary>
    /// The listed price (before VAT is added) for <paramref name="cases"/> cases, and how many cases arrive, with the deal applied.
    /// Discounts never take the price below zero. "Buy N get M free" gives M free cases for every N bought.
    /// </summary>
    public static DealEffect Apply(long casePriceCents, int cases, DealType? type, DealTerms? t)
    {
        var total = casePriceCents * cases;
        var received = cases;
        switch (type)
        {
            case DealType.Discount when t is not null:
                var perCase = t.PercentOff is { } pct ? casePriceCents * pct / 100 : Math.Min(t.CentsOffPerCase ?? 0, casePriceCents);
                total = Math.Max(0, casePriceCents - perCase) * cases;
                break;
            case DealType.FreeGoods when t is { BuyCases: > 0, FreeCases: > 0 }:
                var free = cases / t.BuyCases.Value * t.FreeCases.Value;
                received = cases + free;
                break;
        }
        return new DealEffect(total, received);
    }
}
