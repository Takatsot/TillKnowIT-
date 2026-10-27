using System.Globalization;

namespace Marketplace.Domain;

/// <summary>
/// How offers are ranked. These live in the marketplace settings table, so the platform changes them without a release.
/// Cost is the main weight, then rating, then delivery time. SponsoredBoostPercent is 0 by default: sponsored deals get no help.
/// </summary>
public sealed record RankingWeights(double Cost, double Rating, double Delivery, double SponsoredBoostPercent, decimal VatPercent)
{
    public const string CostKey = "ranking.weight_cost";
    public const string RatingKey = "ranking.weight_rating";
    public const string DeliveryKey = "ranking.weight_delivery";
    public const string SponsoredBoostKey = "ranking.sponsored_boost_percent";
    public const string VatKey = "ranking.vat_percent";
    public static readonly string[] Keys = [CostKey, RatingKey, DeliveryKey, SponsoredBoostKey, VatKey];

    public static RankingWeights Default { get; } = new(1.0, 0.02, 0.01, 0, 15m);

    /// <summary>Whether a value is acceptable for a setting key. Used when an admin changes a setting, so a bad value is refused instead of silently ignored.</summary>
    public static bool IsValid(string key, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v)) return false;
        return key switch
        {
            CostKey or RatingKey or DeliveryKey => v is >= 0 and <= 100,
            SponsoredBoostKey => v is >= 0 and <= 20,
            VatKey => v is >= 0 and <= 30,
            _ => false,
        };
    }

    /// <summary>A missing or unusable setting (not a number, negative, or out of range) falls back to the default.</summary>
    public static RankingWeights From(IReadOnlyDictionary<string, string> settings)
    {
        double Read(string key, double fallback, double min, double max) =>
            settings.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            && double.IsFinite(v) && v >= min && v <= max ? v : fallback;

        return new RankingWeights(
            Read(CostKey, Default.Cost, 0, 100), Read(RatingKey, Default.Rating, 0, 100), Read(DeliveryKey, Default.Delivery, 0, 100),
            Read(SponsoredBoostKey, Default.SponsoredBoostPercent, 0, 20), (decimal)Read(VatKey, (double)Default.VatPercent, 0, 30));
    }
}

public sealed record OfferCandidate(
    Guid SupplierId, string SupplierName, Guid SupplierProductId, int UnitsPerCase, long CasePriceCents, bool VatIncluded,
    long DeliveryFeeCents, long MinOrderCents, bool IsUsual, long? LastPriceCents, double? Rating, int DeliveryDaysAway,
    DealType? DealType, DealTerms? DealTerms, string? DealTitle, bool IsSponsored);

public sealed record RankedOffer(
    int Position, Guid SupplierId, string SupplierName, Guid SupplierProductId, int Cases, int UnitsReceived, long TotalCostCents,
    long TrueCostPerUnitCents, double? MarginPercent, bool IsCheapest, bool IsUsual, long? LastPriceCents, bool MeetsMinimum,
    int DeliveryDaysAway, double? Rating, string? DealTitle, bool IsSponsored, string? Label);

public static class OfferEngine
{
    /// <summary>
    /// M3-1: ranked options for one product the shop needs. The true cost per unit is what the shop really pays for each unit it
    /// receives: the listed price after any deal, plus VAT when the price excludes it, plus the delivery fee, divided by the units
    /// that arrive (free goods included). Usual suppliers come first; everyone is then ordered by a score in which cost dominates.
    /// </summary>
    public static List<RankedOffer> Rank(IEnumerable<OfferCandidate> candidates, int quantityUnits, long? shopSellingPriceCents, RankingWeights weights)
    {
        var priced = candidates.Select(c => Price(c, quantityUnits, shopSellingPriceCents, weights)).ToList();
        if (priced.Count == 0) return [];

        var cheapest = priced.Min(p => p.Offer.TrueCostPerUnitCents);

        double Score(Priced p)
        {
            var effectiveCost = p.Offer.TrueCostPerUnitCents * (p.Offer.IsSponsored ? 1 - weights.SponsoredBoostPercent / 100 : 1);
            var costGap = effectiveCost / cheapest - 1;
            var ratingPenalty = (5 - (p.Offer.Rating ?? 2.5)) / 5;
            return weights.Cost * costGap + weights.Rating * ratingPenalty + weights.Delivery * (p.Offer.DeliveryDaysAway / 7.0);
        }

        return priced
            .OrderByDescending(p => p.Offer.IsUsual)                 // the shop's own supplier first, with its last price
            .ThenBy(Score)
            .ThenBy(p => p.Offer.TrueCostPerUnitCents)
            .ThenBy(p => p.Offer.SupplierName, StringComparer.Ordinal)
            .ThenBy(p => p.Offer.SupplierId)
            .Select((p, i) => p.Offer with { Position = i + 1, IsCheapest = p.Offer.TrueCostPerUnitCents == cheapest })
            .ToList();
    }

    private sealed record Priced(RankedOffer Offer);

    private static Priced Price(OfferCandidate c, int quantityUnits, long? sellingPrice, RankingWeights w)
    {
        var cases = Math.Max(1, (int)Math.Ceiling(quantityUnits / (double)c.UnitsPerCase));
        var effect = DealCalculator.Apply(c.CasePriceCents, cases, c.DealType, c.DealTerms);

        // VAT is added when the supplier's price leaves it out, because a shop that is not VAT-registered cannot claim it back.
        var withVat = c.VatIncluded ? effect.TotalCostCents : RoundHalfUp(effect.TotalCostCents * (100m + w.VatPercent) / 100m);
        var total = withVat + c.DeliveryFeeCents;
        var unitsReceived = effect.CasesReceived * c.UnitsPerCase;
        var trueUnit = RoundHalfUp(total / (decimal)unitsReceived);

        double? margin = sellingPrice is > 0 ? Math.Round((sellingPrice.Value - trueUnit) * 100.0 / sellingPrice.Value, 1) : null;

        return new Priced(new RankedOffer(
            Position: 0, c.SupplierId, c.SupplierName, c.SupplierProductId, cases, unitsReceived, total, trueUnit, margin, IsCheapest: false,
            c.IsUsual, c.LastPriceCents, MeetsMinimum: effect.TotalCostCents >= c.MinOrderCents, c.DeliveryDaysAway, c.Rating,
            c.DealTitle, c.IsSponsored, Label: c.IsSponsored ? "Sponsored" : null));
    }

    private static long RoundHalfUp(decimal value) => (long)Math.Floor(value + 0.5m);
}
