using Marketplace.Domain;

namespace Marketplace.Application.Tests;

public class OfferEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly RankingWeights W = RankingWeights.Default;

    private static OfferCandidate Offer(string name = "Supplier", long casePrice = 18000, int perCase = 24, bool vatIncluded = true, long delivery = 0, long min = 0,
        bool usual = false, long? last = null, double? rating = null, int days = 2, DealType? deal = null, DealTerms? terms = null, string? dealTitle = null, bool sponsored = false) =>
        new(Guid.NewGuid(), name, Guid.NewGuid(), perCase, casePrice, vatIncluded, delivery, min, usual, last, rating, days, deal, terms, dealTitle, sponsored);

    // ---- true cost per unit ----

    [Fact]
    public void A_price_that_includes_VAT_is_not_taxed_again_and_a_price_that_excludes_it_gets_15_percent()
    {
        var included = OfferEngine.Rank([Offer(casePrice: 24000, perCase: 24, vatIncluded: true)], 24, null, W).Single();
        var excluded = OfferEngine.Rank([Offer(casePrice: 24000, perCase: 24, vatIncluded: false)], 24, null, W).Single();

        Assert.Equal(1000, included.TrueCostPerUnitCents);        // R240 / 24 = R10.00
        Assert.Equal(1150, excluded.TrueCostPerUnitCents);        // R276 / 24 = R11.50
        Assert.Equal(27600, excluded.TotalCostCents);
    }

    [Fact]
    public void The_delivery_fee_is_shared_across_every_unit_that_arrives()
    {
        var offer = OfferEngine.Rank([Offer(casePrice: 24000, perCase: 24, delivery: 4800)], 48, null, W).Single();

        Assert.Equal(2, offer.Cases);
        Assert.Equal(48, offer.UnitsReceived);
        Assert.Equal(52800, offer.TotalCostCents);                // 2 x R240 + R48
        Assert.Equal(1100, offer.TrueCostPerUnitCents);           // R528 / 48
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 2)]
    [InlineData(48, 2)]
    [InlineData(49, 3)]
    public void You_buy_whole_cases_so_a_part_case_rounds_up(int unitsNeeded, int casesExpected) =>
        Assert.Equal(casesExpected, OfferEngine.Rank([Offer(perCase: 24)], unitsNeeded, null, W).Single().Cases);

    [Fact]
    public void The_cost_per_unit_is_rounded_to_the_nearest_cent_halves_up()
    {
        // R1.00 case of 8 is 12.5 cents a unit.
        Assert.Equal(13, OfferEngine.Rank([Offer(casePrice: 100, perCase: 8)], 8, null, W).Single().TrueCostPerUnitCents);
    }

    // ---- deals ----

    [Fact]
    public void A_percentage_discount_lowers_the_listed_price()
    {
        var offer = Offer(casePrice: 20000, perCase: 20, deal: DealType.Discount, terms: new DealTerms(PercentOff: 10), dealTitle: "10% off");
        var ranked = OfferEngine.Rank([offer], 20, null, W).Single();

        Assert.Equal(18000, ranked.TotalCostCents);
        Assert.Equal(900, ranked.TrueCostPerUnitCents);
        Assert.Equal("10% off", ranked.DealTitle);
    }

    [Fact]
    public void An_amount_off_a_case_never_takes_the_price_below_zero()
    {
        Assert.Equal(45000, DealCalculator.Apply(20000, 3, DealType.Discount, new DealTerms(CentsOffPerCase: 5000)).TotalCostCents);   // 3 x (R200 - R50)
        Assert.Equal(0, DealCalculator.Apply(20000, 3, DealType.Discount, new DealTerms(CentsOffPerCase: 99999)).TotalCostCents);
    }

    [Fact]
    public void Buy_ten_get_one_free_brings_extra_cases_for_the_same_money()
    {
        var terms = new DealTerms(BuyCases: 10, FreeCases: 1);

        var ten = DealCalculator.Apply(10000, 10, DealType.FreeGoods, terms);
        var twenty = DealCalculator.Apply(10000, 20, DealType.FreeGoods, terms);
        var nine = DealCalculator.Apply(10000, 9, DealType.FreeGoods, terms);

        Assert.Equal((100000L, 11), (ten.TotalCostCents, ten.CasesReceived));
        Assert.Equal((200000L, 22), (twenty.TotalCostCents, twenty.CasesReceived));    // two lots of ten, two free
        Assert.Equal((90000L, 9), (nine.TotalCostCents, nine.CasesReceived));          // not enough bought to earn a free case
    }

    [Fact]
    public void Free_goods_lower_the_true_cost_per_unit_because_more_units_arrive()
    {
        var plain = OfferEngine.Rank([Offer(casePrice: 10000, perCase: 10)], 100, null, W).Single();
        var deal = OfferEngine.Rank([Offer(casePrice: 10000, perCase: 10, deal: DealType.FreeGoods, terms: new DealTerms(BuyCases: 10, FreeCases: 1))], 100, null, W).Single();

        Assert.Equal(1000, plain.TrueCostPerUnitCents);
        Assert.Equal(11 * 10, deal.UnitsReceived);
        Assert.Equal(909, deal.TrueCostPerUnitCents);             // R1000 for 110 units
    }

    // ---- margin and minimum order ----

    [Fact]
    public void The_margin_is_shown_at_the_shops_current_selling_price_and_is_empty_when_there_is_none()
    {
        var offer = Offer(casePrice: 24000, perCase: 24);   // R10.00 a unit
        Assert.Equal(33.3, OfferEngine.Rank([offer], 24, 1500, W).Single().MarginPercent);
        Assert.Equal(-25.0, OfferEngine.Rank([offer], 24, 800, W).Single().MarginPercent);   // selling below cost shows a loss
        Assert.Null(OfferEngine.Rank([offer], 24, null, W).Single().MarginPercent);
        Assert.Null(OfferEngine.Rank([offer], 24, 0, W).Single().MarginPercent);
    }

    [Fact]
    public void An_offer_below_the_suppliers_minimum_order_is_flagged_not_hidden()
    {
        var ranked = OfferEngine.Rank([Offer(casePrice: 18000, min: 50_000)], 24, null, W).Single();
        Assert.False(ranked.MeetsMinimum);
        Assert.True(OfferEngine.Rank([Offer(casePrice: 18000, min: 18_000)], 24, null, W).Single().MeetsMinimum);
    }

    // ---- ranking ----

    [Fact]
    public void The_cheapest_true_cost_comes_first_among_non_usual_suppliers_and_is_marked()
    {
        var ranked = OfferEngine.Rank([
            Offer("Dear", casePrice: 24000), Offer("Cheap", casePrice: 18000), Offer("Middle", casePrice: 20000),
        ], 24, null, W);

        Assert.Equal(["Cheap", "Middle", "Dear"], ranked.Select(r => r.SupplierName));
        Assert.Equal([true, false, false], ranked.Select(r => r.IsCheapest));
        Assert.Equal([1, 2, 3], ranked.Select(r => r.Position));
    }

    [Fact]
    public void Delivery_and_VAT_can_make_the_listed_cheapest_supplier_the_dearer_one()
    {
        var ranked = OfferEngine.Rank([
            Offer("Low price, high delivery", casePrice: 20000, perCase: 24, delivery: 15000),     // R200 + R150 delivery on one case
            Offer("Higher price, free delivery", casePrice: 22000, perCase: 24, delivery: 0),
            Offer("Low price, VAT extra", casePrice: 19000, perCase: 24, vatIncluded: false),      // R190 + 15% = R218.50
        ], 24, null, W);

        Assert.Equal(["Low price, VAT extra", "Higher price, free delivery", "Low price, high delivery"], ranked.Select(r => r.SupplierName));
    }

    [Fact]
    public void The_usual_supplier_is_first_with_its_last_price_even_when_not_the_cheapest_which_is_still_marked()
    {
        var ranked = OfferEngine.Rank([
            Offer("Cheap", casePrice: 18000),
            Offer("My usual", casePrice: 21000, usual: true, last: 20500),
        ], 24, null, W);

        Assert.Equal("My usual", ranked[0].SupplierName);
        Assert.Equal(20500, ranked[0].LastPriceCents);
        Assert.False(ranked[0].IsCheapest);
        Assert.True(ranked[1].IsCheapest);
    }

    [Fact]
    public void Rating_then_delivery_time_break_ties_when_cost_is_equal()
    {
        var ranked = OfferEngine.Rank([
            Offer("Slow", rating: 4.5, days: 6), Offer("Poor rating", rating: 2.0, days: 1), Offer("Best", rating: 4.5, days: 1),
        ], 24, null, W);

        Assert.Equal(["Best", "Slow", "Poor rating"], ranked.Select(r => r.SupplierName));
    }

    [Fact]
    public void A_small_rating_difference_does_not_outweigh_a_real_price_difference_by_default()
    {
        var ranked = OfferEngine.Rank([Offer("Five stars", casePrice: 18500, rating: 5), Offer("Cheaper, unrated", casePrice: 18000)], 24, null, W);
        Assert.Equal("Cheaper, unrated", ranked[0].SupplierName);
    }

    [Fact]
    public void The_weights_come_from_settings_and_bad_values_fall_back_to_the_defaults()
    {
        Assert.Equal(RankingWeights.Default, RankingWeights.From(new Dictionary<string, string>()));
        var custom = RankingWeights.From(new Dictionary<string, string> { ["ranking.weight_rating"] = "2.5", ["ranking.vat_percent"] = "0" });
        Assert.Equal((2.5, 0m), (custom.Rating, custom.VatPercent));
        var bad = RankingWeights.From(new Dictionary<string, string> { ["ranking.weight_cost"] = "-1", ["ranking.sponsored_boost_percent"] = "90", ["ranking.weight_delivery"] = "lots" });
        Assert.Equal(RankingWeights.Default, bad);

        // With rating weighted heavily, a five-star supplier now beats a slightly cheaper unrated one.
        var heavy = RankingWeights.From(new Dictionary<string, string> { ["ranking.weight_rating"] = "50" });
        var ranked = OfferEngine.Rank([Offer("Five stars", casePrice: 18500, rating: 5), Offer("Cheaper, unrated", casePrice: 18000)], 24, null, heavy);
        Assert.Equal("Five stars", ranked[0].SupplierName);
    }

    [Fact]
    public void Nothing_to_rank_gives_an_empty_list_and_ranking_is_deterministic()
    {
        Assert.Empty(OfferEngine.Rank([], 24, null, W));

        var offers = new[] { Offer("A"), Offer("B"), Offer("C") };
        Assert.Equal(OfferEngine.Rank(offers, 24, 1500, W).Select(o => o.SupplierId), OfferEngine.Rank(offers.Reverse(), 24, 1500, W).Select(o => o.SupplierId));
    }

    // ---- fairness: sponsored deals ----

    [Fact]
    public void Sponsored_offers_are_always_labelled_and_other_offers_never_are()
    {
        var ranked = OfferEngine.Rank([Offer("Paid", sponsored: true, dealTitle: "Summer special"), Offer("Plain")], 24, null, W);

        Assert.Equal("Sponsored", ranked.Single(r => r.SupplierName == "Paid").Label);
        Assert.Null(ranked.Single(r => r.SupplierName == "Plain").Label);
    }

    [Fact]
    public void With_default_settings_a_sponsored_offer_never_outranks_a_cheaper_one_that_is_not_sponsored_checked_over_many_random_cases()
    {
        var random = new Random(20261026);
        for (var trial = 0; trial < 500; trial++)
        {
            var offers = Enumerable.Range(0, random.Next(2, 9)).Select(i => Offer(
                $"S{i}", casePrice: random.Next(5_000, 40_000), perCase: random.Next(1, 49), vatIncluded: random.Next(2) == 0,
                delivery: random.Next(0, 3) == 0 ? 0 : random.Next(1_000, 12_000), rating: random.Next(4) == 0 ? null : random.Next(1, 6),
                days: random.Next(0, 8), sponsored: random.Next(3) == 0,
                deal: random.Next(4) == 0 ? DealType.Discount : null, terms: new DealTerms(PercentOff: random.Next(1, 40)))).ToList();

            var ranked = OfferEngine.Rank(offers, random.Next(1, 200), 2000, W);

            foreach (var sponsored in ranked.Where(r => r.IsSponsored))
                foreach (var plain in ranked.Where(r => !r.IsSponsored && r.TrueCostPerUnitCents < sponsored.TrueCostPerUnitCents))
                    Assert.True(plain.Position < sponsored.Position,
                        $"trial {trial}: sponsored {sponsored.SupplierName} ({sponsored.TrueCostPerUnitCents}) outranked cheaper {plain.SupplierName} ({plain.TrueCostPerUnitCents})");
        }
    }

    [Fact]
    public void A_boost_if_the_platform_ever_turns_it_on_helps_only_within_that_percentage_and_the_label_stays()
    {
        var boosted = W with { SponsoredBoostPercent = 5 };
        var close = OfferEngine.Rank([Offer("Plain", casePrice: 20000), Offer("Paid close", casePrice: 20500, sponsored: true)], 24, null, boosted);
        var far = OfferEngine.Rank([Offer("Plain", casePrice: 20000), Offer("Paid far", casePrice: 24000, sponsored: true)], 24, null, boosted);

        Assert.Equal("Paid close", close[0].SupplierName);                 // 2.5% dearer, inside the 5% boost
        Assert.Equal("Sponsored", close[0].Label);
        Assert.Equal("Plain", far[0].SupplierName);                        // 20% dearer: the boost cannot buy that
    }

    // ---- deals as data ----

    private static DealInput Input(DealType type = DealType.Discount, DealTerms? terms = null, int startDays = 0, int endDays = 30, string[]? areas = null, bool sponsored = false, string title = "Spring special") =>
        new(title, type, terms ?? new DealTerms(SupplierProductId: Guid.NewGuid(), PercentOff: 10), Now.AddDays(startDays), Now.AddDays(endDays), areas ?? ["jhb"], sponsored);

    private static readonly string[] Served = ["jhb", "cpt"];

    [Fact]
    public void A_deal_is_active_only_between_its_dates_and_for_its_areas_and_not_once_cancelled()
    {
        var deal = Deal.Create(Guid.NewGuid(), Input(startDays: 1, endDays: 10), Served, Now).Value!;

        Assert.False(deal.IsActiveAt(Now));
        Assert.True(deal.IsActiveAt(Now.AddDays(1)));
        Assert.True(deal.IsActiveAt(Now.AddDays(9)));
        Assert.False(deal.IsActiveAt(Now.AddDays(10)));
        Assert.True(deal.AppliesToArea("jhb"));
        Assert.False(deal.AppliesToArea("cpt"));

        deal.Cancel();
        Assert.False(deal.IsActiveAt(Now.AddDays(5)));
    }

    [Fact]
    public void A_deal_needs_a_title_sensible_dates_and_areas_the_supplier_serves()
    {
        string Code(DealInput i) => Deal.Create(Guid.NewGuid(), i, Served, Now).Error!.Code;

        Assert.Equal("deal.title_invalid", Code(Input(title: " ")));
        Assert.Equal("deal.dates_invalid", Code(Input(startDays: 5, endDays: 5)));
        Assert.Equal("deal.already_over", Code(Input(startDays: -10, endDays: -1)));
        Assert.Equal("deal.too_long", Code(Input(endDays: 181)));
        Assert.Equal("deal.areas_required", Code(Input(areas: [])));
        Assert.Equal("deal.area_not_served", Code(Input(areas: ["eth"])));
    }

    [Fact]
    public void Each_deal_type_needs_its_own_terms()
    {
        string Code(DealType t, DealTerms terms) => Deal.Create(Guid.NewGuid(), Input(t, terms), Served, Now).Error!.Code;
        var product = Guid.NewGuid();

        Assert.Equal("deal.product_required", Code(DealType.Discount, new DealTerms(PercentOff: 10)));
        Assert.Equal("deal.discount_invalid", Code(DealType.Discount, new DealTerms(SupplierProductId: product)));
        Assert.Equal("deal.discount_invalid", Code(DealType.Discount, new DealTerms(SupplierProductId: product, PercentOff: 10, CentsOffPerCase: 500)));
        Assert.Equal("deal.discount_invalid", Code(DealType.Discount, new DealTerms(SupplierProductId: product, PercentOff: 95)));
        Assert.Equal("deal.free_goods_invalid", Code(DealType.FreeGoods, new DealTerms(SupplierProductId: product, BuyCases: 10)));
        Assert.Equal("deal.combo_invalid", Code(DealType.Combo, new DealTerms(Items: [new ComboItem(product, 1)], ComboPriceCents: 5000)));
        Assert.Equal("deal.combo_invalid", Code(DealType.Combo, new DealTerms(Items: [new ComboItem(product, 1), new ComboItem(product, 2)], ComboPriceCents: 5000)));
        Assert.Equal("deal.combo_invalid", Code(DealType.Combo, new DealTerms(Items: [new ComboItem(product, 1), new ComboItem(Guid.NewGuid(), 1)])));

        Assert.True(Deal.Create(Guid.NewGuid(), Input(DealType.FreeGoods, new DealTerms(SupplierProductId: product, BuyCases: 10, FreeCases: 1)), Served, Now).IsSuccess);
        Assert.True(Deal.Create(Guid.NewGuid(), Input(DealType.Combo, new DealTerms(Items: [new ComboItem(product, 1), new ComboItem(Guid.NewGuid(), 2)], ComboPriceCents: 9000)), Served, Now).IsSuccess);
    }

    [Fact]
    public void Deal_terms_survive_being_stored_and_a_sponsored_deal_stays_sponsored()
    {
        var product = Guid.NewGuid();
        var deal = Deal.Create(Guid.NewGuid(), Input(DealType.FreeGoods, new DealTerms(SupplierProductId: product, BuyCases: 10, FreeCases: 1), sponsored: true), Served, Now).Value!;

        Assert.Equal((product, 10, 1), (deal.Terms.SupplierProductId!.Value, deal.Terms.BuyCases!.Value, deal.Terms.FreeCases!.Value));
        Assert.True(deal.IsSponsored);
    }
}
