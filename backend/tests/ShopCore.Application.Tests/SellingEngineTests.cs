using ShopCore.Domain;
using ShopCore.Domain.Selling;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public class WeightPricingTests
{
    [Fact]
    public void A_weighed_line_is_the_net_weight_at_the_price_per_kilogram_rounded_half_up()
    {
        Assert.Equal(8_750, WeightPricing.LineTotal(12_500, 700).Value);         // 0.7 kg at R125.00
        Assert.Equal(1_250, WeightPricing.LineTotal(12_500, 100).Value);
        Assert.Equal(1, WeightPricing.LineTotal(100, 5).Value);                  // 0.5 cent rounds up
        Assert.Equal(0, WeightPricing.LineTotal(100, 4).Value);                  // 0.4 cent rounds down
        Assert.Equal(7_500, WeightPricing.LineTotal(12_500, 700, tareGrams: 100).Value);   // 600 g net
    }

    [Fact]
    public void Weight_price_and_tare_are_checked()
    {
        Assert.Equal("weight.weight_invalid", WeightPricing.LineTotal(100, 0).Error!.Code);
        Assert.Equal("weight.weight_invalid", WeightPricing.LineTotal(100, 50_001).Error!.Code);
        Assert.Equal("weight.tare_invalid", WeightPricing.LineTotal(100, 500, 500).Error!.Code);
        Assert.Equal("weight.tare_invalid", WeightPricing.LineTotal(100, 500, -1).Error!.Code);
        Assert.Equal("weight.price_invalid", WeightPricing.LineTotal(-1, 500).Error!.Code);
    }
}

public class EmbeddedBarcodeTests
{
    private static readonly EmbeddedBarcodeRules Weight = new("2", 6, 5, EmbeddedValueType.Weight, 3);
    private static readonly EmbeddedBarcodeRules Price = new("2", 6, 5, EmbeddedValueType.Price, 2);

    [Fact]
    public void The_check_digit_is_the_ean_13_one()
    {
        Assert.Equal(1, EmbeddedBarcodes.CheckDigit("400638133393"));   // 4006381333931 is a real EAN-13
        Assert.True(EmbeddedBarcodes.HasValidCheckDigit("4006381333931"));
        Assert.False(EmbeddedBarcodes.HasValidCheckDigit("4006381333932"));
        Assert.False(EmbeddedBarcodes.HasValidCheckDigit("400638133393"));
        Assert.False(EmbeddedBarcodes.HasValidCheckDigit("40063813339X1"));
    }

    [Fact]
    public void A_scale_label_gives_back_the_item_and_the_weight_in_grams()
    {
        var code = EmbeddedBarcodes.Build(Weight, "000123", 750);   // 0.750 kg

        var read = EmbeddedBarcodes.Parse(code, Weight)!;

        Assert.Equal(("000123", 750, null), (read.ItemCode, read.WeightGrams, read.PriceCents));
        Assert.Equal(13, code.Length);
    }

    [Fact]
    public void A_price_label_gives_back_the_price_in_cents()
    {
        var read = EmbeddedBarcodes.Parse(EmbeddedBarcodes.Build(Price, "000045", 4_999), Price)!;

        Assert.Equal(("000045", null, 4_999L), (read.ItemCode, read.WeightGrams, read.PriceCents));
    }

    [Fact]
    public void Decimals_say_how_the_value_is_scaled()
    {
        Assert.Equal(2_000, EmbeddedBarcodes.Parse(EmbeddedBarcodes.Build(Weight with { Decimals = 0 }, "1", 2), Weight with { Decimals = 0 })!.WeightGrams);   // 2 kg
        Assert.Equal(500, EmbeddedBarcodes.Parse(EmbeddedBarcodes.Build(Weight with { Decimals = 1 }, "1", 5), Weight with { Decimals = 1 })!.WeightGrams);     // 0.5 kg
        Assert.Equal(12_00, EmbeddedBarcodes.Parse(EmbeddedBarcodes.Build(Price with { Decimals = 0 }, "1", 12), Price with { Decimals = 0 })!.PriceCents);      // R12
    }

    [Fact]
    public void An_ordinary_barcode_a_wrong_check_digit_a_zero_value_or_an_absurd_weight_is_not_a_label()
    {
        var good = EmbeddedBarcodes.Build(Weight, "000123", 750);

        Assert.Null(EmbeddedBarcodes.Parse("6001234567890", Weight));                                   // an ordinary product
        Assert.Null(EmbeddedBarcodes.Parse(good[..12] + (good[12] == '0' ? '1' : '0'), Weight));       // check digit wrong
        Assert.Null(EmbeddedBarcodes.Parse(EmbeddedBarcodes.Build(Weight, "000123", 0), Weight));       // nothing on the scale
        Assert.Null(EmbeddedBarcodes.Parse(EmbeddedBarcodes.Build(Weight, "000123", 99_999), Weight));  // 99.999 kg
        Assert.Null(EmbeddedBarcodes.Parse(null, Weight));
        Assert.Null(EmbeddedBarcodes.Parse("", Weight));
    }

    [Fact]
    public void Rules_are_read_from_the_pack_settings_and_must_fit_thirteen_digits()
    {
        var ok = EmbeddedBarcodeRules.FromJson("{\"prefix\":\"2\",\"itemDigits\":6,\"valueDigits\":5,\"valueType\":\"weight\",\"decimals\":3}");
        var price = EmbeddedBarcodeRules.FromJson("{\"prefix\":\"28\",\"itemDigits\":4,\"valueDigits\":6,\"valueType\":\"price\"}");

        Assert.Equal(Weight, ok.Value);
        Assert.Equal((EmbeddedValueType.Price, 2), (price.Value!.ValueType, price.Value.Decimals));   // price defaults to 2 decimals
        Assert.Equal("barcode.rules_invalid", EmbeddedBarcodeRules.FromJson("{\"prefix\":\"2\",\"itemDigits\":7,\"valueDigits\":5,\"valueType\":\"weight\"}").Error!.Code);
        Assert.Equal("barcode.rules_invalid", EmbeddedBarcodeRules.FromJson("{\"prefix\":\"x\",\"itemDigits\":6,\"valueDigits\":5,\"valueType\":\"weight\"}").Error!.Code);
        Assert.Equal("barcode.rules_invalid", EmbeddedBarcodeRules.FromJson("not json").Error!.Code);
        Assert.Equal("barcode.rules_invalid", EmbeddedBarcodeRules.FromJson("{\"prefix\":\"2\"}").Error!.Code);
        Assert.Equal("barcode.rules_missing", EmbeddedBarcodeRules.FromJson(null).Error!.Code);
    }

    [Fact]
    public void Every_built_label_reads_back_to_what_went_in()
    {
        var random = new Random(5);
        for (var i = 0; i < 1_000; i++)
        {
            var item = random.Next(0, 999_999).ToString("D6");
            var grams = random.Next(1, 50_000);
            var read = EmbeddedBarcodes.Parse(EmbeddedBarcodes.Build(Weight, item, grams), Weight)!;
            Assert.Equal((item, grams), (read.ItemCode, read.WeightGrams!.Value));
        }
    }
}

public class ModifierTests
{
    private static readonly ModifierOption Cheese = new(Guid.NewGuid(), "Cheese", 500);
    private static readonly ModifierOption Bacon = new(Guid.NewGuid(), "Bacon", 1_200);
    private static readonly ModifierOption Mayo = new(Guid.NewGuid(), "Mayo", 0);
    private static readonly ModifierOption Ketchup = new(Guid.NewGuid(), "Ketchup", 0);
    private static readonly ModifierGroupDef Extras = new(Guid.NewGuid(), "Extras", false, 0, 2, [Cheese, Bacon]);
    private static readonly ModifierGroupDef Sauce = new(Guid.NewGuid(), "Sauce", true, 1, 1, [Mayo, Ketchup]);

    [Fact]
    public void Chosen_options_add_their_prices_and_optional_groups_may_be_left_empty()
    {
        Assert.Equal(1_700, ModifierSelection.Price([Extras, Sauce], [Cheese.Id, Bacon.Id, Mayo.Id]).Value);
        Assert.Equal(0, ModifierSelection.Price([Extras, Sauce], [Ketchup.Id]).Value);
    }

    [Fact]
    public void A_required_group_must_be_chosen_from_and_at_most_the_maximum()
    {
        Assert.Equal("modifier.too_few", ModifierSelection.Price([Extras, Sauce], [Cheese.Id]).Error!.Code);
        Assert.Equal("modifier.too_many", ModifierSelection.Price([Sauce], [Mayo.Id, Ketchup.Id]).Error!.Code);
        Assert.Equal("modifier.too_many", ModifierSelection.Price([Extras with { Max = 1 }, Sauce], [Cheese.Id, Bacon.Id, Mayo.Id]).Error!.Code);
    }

    [Fact]
    public void An_option_from_another_item_or_chosen_twice_is_refused()
    {
        Assert.Equal("modifier.unknown", ModifierSelection.Price([Sauce], [Mayo.Id, Guid.NewGuid()]).Error!.Code);
        Assert.Equal("modifier.repeated", ModifierSelection.Price([Sauce], [Mayo.Id, Mayo.Id]).Error!.Code);
    }

    [Fact]
    public void A_group_needs_a_name_sensible_limits_and_options()
    {
        Assert.Equal("modifier.name_invalid", ModifierGroupDef.Validate(" ", false, 0, 1, 2).Error!.Code);
        Assert.Equal("modifier.limits_invalid", ModifierGroupDef.Validate("Sauce", false, 2, 1, 2).Error!.Code);
        Assert.Equal("modifier.limits_invalid", ModifierGroupDef.Validate("Sauce", true, 0, 1, 2).Error!.Code);
        Assert.Equal("modifier.limits_invalid", ModifierGroupDef.Validate("Sauce", false, 0, 21, 2).Error!.Code);
        Assert.Equal("modifier.options_invalid", ModifierGroupDef.Validate("Sauce", false, 0, 1, 0).Error!.Code);
        Assert.True(ModifierGroupDef.Validate("Sauce", true, 1, 1, 2).IsSuccess);
    }
}

public class PromotionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, BusinessDay.Offset);
    private static readonly DateTimeOffset End = new(2026, 12, 1, 0, 0, 0, BusinessDay.Offset);
    private static readonly DateTimeOffset MondayNoon = new(2026, 10, 26, 12, 0, 0, BusinessDay.Offset);
    private static readonly Guid Burger = Guid.NewGuid();
    private static readonly Guid Chips = Guid.NewGuid();
    private static readonly Guid Cola = Guid.NewGuid();

    private static PromotionDef Percent(int pct, Guid? product = null, Weekdays days = Weekdays.All, TimeOnly? from = null, TimeOnly? to = null) =>
        new(Guid.NewGuid(), $"{pct}% off", PromotionType.PercentOff, product is null ? [] : [product.Value], pct, 0, [], Start, End, days, from, to);

    private static PromotionDef Combo(long price, params (Guid Product, int Qty)[] items) =>
        new(Guid.NewGuid(), "Combo", PromotionType.Combo, [], 0, price, [.. items.Select(i => new ComboItem(i.Product, i.Qty))], Start, End, Weekdays.All, null, null);

    private static BasketLine Line(Guid product, int qty, long price) => new(product, qty, price);

    // ---- when a promotion runs ----

    [Fact]
    public void A_promotion_runs_between_its_dates_on_its_days_and_inside_its_hours_in_south_african_time()
    {
        var happyHour = Percent(20, null, Weekdays.Mon | Weekdays.Tue, new TimeOnly(16, 0), new TimeOnly(18, 0));

        Assert.False(happyHour.IsRunning(new DateTimeOffset(2026, 10, 26, 15, 59, 0, BusinessDay.Offset)));
        Assert.True(happyHour.IsRunning(new DateTimeOffset(2026, 10, 26, 16, 0, 0, BusinessDay.Offset)));
        Assert.False(happyHour.IsRunning(new DateTimeOffset(2026, 10, 26, 18, 0, 0, BusinessDay.Offset)));   // To is exclusive
        Assert.False(happyHour.IsRunning(new DateTimeOffset(2026, 10, 28, 17, 0, 0, BusinessDay.Offset)));   // a Wednesday
        Assert.True(happyHour.IsRunning(new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero)));        // 14:30 UTC is 16:30 in South Africa
        Assert.False(happyHour.IsRunning(new DateTimeOffset(2026, 9, 29, 17, 0, 0, BusinessDay.Offset)));    // before it starts
        Assert.False(happyHour.IsRunning(End));
    }

    [Fact]
    public void A_window_that_runs_past_midnight_covers_the_small_hours()
    {
        var late = Percent(10, null, Weekdays.All, new TimeOnly(22, 0), new TimeOnly(2, 0));

        Assert.True(late.IsRunning(new DateTimeOffset(2026, 10, 26, 23, 0, 0, BusinessDay.Offset)));
        Assert.True(late.IsRunning(new DateTimeOffset(2026, 10, 27, 1, 0, 0, BusinessDay.Offset)));
        Assert.False(late.IsRunning(new DateTimeOffset(2026, 10, 27, 12, 0, 0, BusinessDay.Offset)));
    }

    [Fact]
    public void An_inactive_promotion_never_runs()
    {
        Assert.False((Percent(10) with { IsActive = false }).IsRunning(MondayNoon));
    }

    // ---- what it takes off ----

    [Fact]
    public void A_percentage_comes_off_each_unit_rounded_half_up()
    {
        var priced = PromotionEngine.Price([Line(Burger, 3, 4_999)], [Percent(10, Burger)], MondayNoon);

        Assert.Equal(3 * 500, priced.DiscountCents);               // 10% of 4999 is 499.9, which is 500
        Assert.Equal(14_997 - 1_500, priced.TotalCents);
        Assert.Equal(1_500, priced.DiscountsByProduct[Burger]);
    }

    [Fact]
    public void An_amount_off_a_fixed_price_and_a_percentage_each_do_their_own_job()
    {
        var amountOff = new PromotionDef(Guid.NewGuid(), "R5 off", PromotionType.AmountOff, [Burger], 0, 500, [], Start, End, Weekdays.All, null, null);
        var fixedPrice = new PromotionDef(Guid.NewGuid(), "Chips R15", PromotionType.FixedPrice, [Chips], 0, 1_500, [], Start, End, Weekdays.All, null, null);

        var priced = PromotionEngine.Price([Line(Burger, 2, 4_500), Line(Chips, 1, 2_000), Line(Cola, 1, 1_000)], [amountOff, fixedPrice], MondayNoon);

        Assert.Equal((1_000L, 500L), (priced.DiscountsByProduct[Burger], priced.DiscountsByProduct[Chips]));
        Assert.False(priced.DiscountsByProduct.ContainsKey(Cola));
        Assert.Equal(12_000 - 1_500, priced.TotalCents);
    }

    [Fact]
    public void An_amount_off_is_never_more_than_the_item_costs_and_a_fixed_price_above_the_price_does_nothing()
    {
        var big = new PromotionDef(Guid.NewGuid(), "R100 off", PromotionType.AmountOff, [Cola], 0, 10_000, [], Start, End, Weekdays.All, null, null);
        var dearer = new PromotionDef(Guid.NewGuid(), "Chips R50", PromotionType.FixedPrice, [Chips], 0, 5_000, [], Start, End, Weekdays.All, null, null);

        var priced = PromotionEngine.Price([Line(Cola, 2, 1_000), Line(Chips, 1, 2_000)], [big, dearer], MondayNoon);

        Assert.Equal(2_000, priced.DiscountCents);   // both colas free, chips unchanged
        Assert.Equal(2_000, priced.TotalCents);
    }

    [Fact]
    public void Promotions_do_not_stack_the_one_that_saves_most_wins_for_each_product()
    {
        var small = Percent(10, Burger);
        var big = Percent(30, Burger);

        var priced = PromotionEngine.Price([Line(Burger, 1, 10_000)], [small, big], MondayNoon);

        Assert.Equal(3_000, priced.DiscountCents);
        Assert.Equal(big.Id, Assert.Single(priced.Applied).PromotionId);
    }

    [Fact]
    public void A_promotion_for_all_products_applies_to_every_line()
    {
        var priced = PromotionEngine.Price([Line(Burger, 1, 4_000), Line(Cola, 2, 1_000)], [Percent(50)], MondayNoon);

        Assert.Equal(3_000, priced.DiscountCents);
    }

    // ---- combos ----

    [Fact]
    public void A_combo_sells_the_set_for_its_price_and_the_saving_lands_on_the_items()
    {
        var meal = Combo(6_000, (Burger, 1), (Chips, 1), (Cola, 1));   // normally 4500 + 2000 + 1000 = 7500

        var priced = PromotionEngine.Price([Line(Burger, 1, 4_500), Line(Chips, 1, 2_000), Line(Cola, 1, 1_000)], [meal], MondayNoon);

        Assert.Equal((1_500L, 6_000L), (priced.DiscountCents, priced.TotalCents));
        Assert.Equal(1_500, priced.DiscountsByProduct.Values.Sum());   // the per-line shares add up to exactly the saving
        Assert.Equal(1, Assert.Single(priced.Applied).Times);
    }

    [Fact]
    public void A_combo_applies_as_many_times_as_the_basket_allows_and_leaves_the_rest_alone()
    {
        var meal = Combo(5_000, (Burger, 1), (Cola, 1));   // 4500 + 1000 = 5500, saves 500

        var priced = PromotionEngine.Price([Line(Burger, 3, 4_500), Line(Cola, 2, 1_000)], [meal], MondayNoon);

        Assert.Equal(1_000, priced.DiscountCents);   // twice; the third burger has no cola to go with
        Assert.Equal(2, priced.Applied.Single().Times);
    }

    [Fact]
    public void A_unit_used_in_a_combo_is_not_discounted_again_by_a_line_promotion()
    {
        var meal = Combo(5_000, (Burger, 1), (Cola, 1));
        var colaHalf = Percent(50, Cola);

        var priced = PromotionEngine.Price([Line(Burger, 1, 4_500), Line(Cola, 2, 1_000)], [meal, colaHalf], MondayNoon);

        // One cola is in the combo (saving 500); the other cola takes the 50% (500). The combo cola is not halved as well.
        Assert.Equal(1_000, priced.DiscountCents);
    }

    [Fact]
    public void A_combo_that_does_not_save_anything_is_not_applied_and_an_incomplete_basket_gets_nothing()
    {
        var dear = Combo(9_000, (Burger, 1), (Cola, 1));
        var meal = Combo(5_000, (Burger, 1), (Cola, 1));

        Assert.Equal(0, PromotionEngine.Price([Line(Burger, 1, 4_500), Line(Cola, 1, 1_000)], [dear], MondayNoon).DiscountCents);
        Assert.Equal(0, PromotionEngine.Price([Line(Burger, 1, 4_500)], [meal], MondayNoon).DiscountCents);
    }

    [Fact]
    public void With_no_running_promotion_the_basket_comes_back_unchanged()
    {
        var expired = Percent(50) with { EndsAt = Start.AddDays(1) };

        var priced = PromotionEngine.Price([Line(Burger, 2, 4_000)], [expired], MondayNoon);

        Assert.Equal((8_000L, 0L, 8_000L), (priced.SubtotalCents, priced.DiscountCents, priced.TotalCents));
        Assert.Empty(priced.Applied);
        Assert.Equal(0, PromotionEngine.Price([], [Percent(10)], MondayNoon).TotalCents);
    }

    [Fact]
    public void For_any_basket_and_promotions_the_discount_is_never_more_than_the_subtotal_and_the_parts_add_up()
    {
        var random = new Random(99);
        var products = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        for (var round = 0; round < 500; round++)
        {
            var basket = products.Where(_ => random.Next(2) == 0).Select(p => Line(p, random.Next(1, 6), random.Next(1, 20) * 100L + random.Next(0, 100))).ToList();
            var promos = new List<PromotionDef>();
            for (var k = 0; k < random.Next(0, 5); k++)
            {
                var pick = products.OrderBy(_ => random.Next()).Take(random.Next(2, 4)).ToList();
                promos.Add(random.Next(3) switch
                {
                    0 => Percent(random.Next(1, 101), random.Next(2) == 0 ? null : pick[0]),
                    1 => Combo(random.Next(100, 6_000), [.. pick.Select(p => (p, random.Next(1, 3)))]),
                    _ => new PromotionDef(Guid.NewGuid(), "off", PromotionType.AmountOff, [pick[0]], 0, random.Next(1, 3_000), [], Start, End, Weekdays.All, null, null),
                });
            }

            var priced = PromotionEngine.Price(basket, promos, MondayNoon);

            Assert.InRange(priced.DiscountCents, 0, priced.SubtotalCents);
            Assert.Equal(priced.SubtotalCents - priced.DiscountCents, priced.TotalCents);
            Assert.Equal(priced.DiscountCents, priced.DiscountsByProduct.Values.Sum());
            Assert.Equal(priced.DiscountCents, priced.Applied.Sum(a => a.DiscountCents));
            foreach (var (product, off) in priced.DiscountsByProduct)
                Assert.True(off <= basket.Where(l => l.ProductId == product).Sum(l => l.UnitPriceCents * l.Quantity), "a line is never discounted below zero");
            Assert.Equal(priced.TotalCents, PromotionEngine.Price(basket, promos, MondayNoon).TotalCents);   // the same answer every time
        }
    }

    [Fact]
    public void A_promotion_is_validated_before_it_is_saved()
    {
        Result Check(PromotionType type, string name = "Deal", int pct = 10, long cents = 100, int products = 1, int items = 2, int days = 127, bool endsAfter = true)
        {
            var ids = Enumerable.Range(0, products).Select(_ => Guid.NewGuid()).ToList();
            var combo = Enumerable.Range(0, items).Select(_ => new ComboItem(Guid.NewGuid(), 1)).ToList();
            return PromotionDef.Validate(type, name, ids, pct, cents, combo, Start, endsAfter ? End : Start, (Weekdays)days);
        }

        Assert.True(Check(PromotionType.PercentOff).IsSuccess);
        Assert.Equal("promotion.name_invalid", Check(PromotionType.PercentOff, " ").Error!.Code);
        Assert.Equal("promotion.dates_invalid", Check(PromotionType.PercentOff, endsAfter: false).Error!.Code);
        Assert.Equal("promotion.days_invalid", Check(PromotionType.PercentOff, days: 0).Error!.Code);
        Assert.Equal("promotion.percent_invalid", Check(PromotionType.PercentOff, pct: 101).Error!.Code);
        Assert.Equal("promotion.amount_invalid", Check(PromotionType.AmountOff, cents: 0).Error!.Code);
        Assert.Equal("promotion.products_required", Check(PromotionType.FixedPrice, products: 0).Error!.Code);
        Assert.Equal("promotion.combo_items", Check(PromotionType.Combo, items: 1).Error!.Code);
        Assert.Equal("promotion.amount_invalid", Check(PromotionType.Combo, cents: 0).Error!.Code);
    }
}

public class SplitPaymentTests
{
    private static PaymentPart Cash(long amount, long? tendered = null) => new(PaymentMethods.Cash, amount, TenderedCents: tendered);

    [Fact]
    public void Parts_must_add_up_to_exactly_what_is_owed()
    {
        Assert.True(SplitPayments.Check([Cash(3_000), new(PaymentMethods.Card, 2_000)], 5_000).IsSuccess);
        Assert.Equal("payment.total_mismatch", SplitPayments.Check([Cash(3_000), new(PaymentMethods.Card, 1_999)], 5_000).Error!.Code);
        Assert.Equal("payment.total_mismatch", SplitPayments.Check([Cash(3_000), new(PaymentMethods.Card, 2_001)], 5_000).Error!.Code);
    }

    [Fact]
    public void Change_is_worked_out_from_the_cash_handed_over_and_only_for_cash()
    {
        Assert.Equal(1_000, SplitPayments.Check([Cash(3_000, 4_000), new(PaymentMethods.Qr, 2_000)], 5_000).Value);
        Assert.Equal(0, SplitPayments.Check([Cash(3_000), new(PaymentMethods.Qr, 2_000)], 5_000).Value);
        Assert.Equal("payment.tender_invalid", SplitPayments.Check([Cash(3_000, 2_000), new(PaymentMethods.Qr, 2_000)], 5_000).Error!.Code);
        Assert.Equal("payment.tender_invalid", SplitPayments.Check([Cash(3_000), new PaymentPart(PaymentMethods.Qr, 2_000, TenderedCents: 3_000)], 5_000).Error!.Code);
    }

    [Fact]
    public void Each_method_is_used_once_every_part_is_positive_and_the_number_of_parts_is_limited()
    {
        Assert.Equal("payment.method_repeated", SplitPayments.Check([Cash(1_000), Cash(1_000)], 2_000).Error!.Code);
        Assert.Equal("payment.amount_invalid", SplitPayments.Check([Cash(5_000), new(PaymentMethods.Card, 0)], 5_000).Error!.Code);
        Assert.Equal("payment.method_invalid", SplitPayments.Check([new("barter", 5_000)], 5_000).Error!.Code);
        Assert.Equal("payment.parts_required", SplitPayments.Check([], 5_000).Error!.Code);

        var seven = new[] { "cash", "card", "qr", "credit", "wallet", "voucher", "layby" }.Select(m => new PaymentPart(m, 100)).ToList();
        Assert.Equal("payment.too_many_parts", SplitPayments.Check(seven, 700).Error!.Code);
    }

    [Fact]
    public void Credit_needs_an_account_and_wallet_and_voucher_parts_need_a_reference()
    {
        Assert.Equal("payment.account_required", SplitPayments.Check([new(PaymentMethods.Credit, 5_000)], 5_000).Error!.Code);
        Assert.Equal("payment.account_unexpected", SplitPayments.Check([new PaymentPart(PaymentMethods.Card, 5_000, AccountId: Guid.NewGuid())], 5_000).Error!.Code);
        Assert.True(SplitPayments.Check([new PaymentPart(PaymentMethods.Credit, 5_000, AccountId: Guid.NewGuid())], 5_000).IsSuccess);
        Assert.Equal("payment.reference_required", SplitPayments.Check([new(ExtraPaymentMethods.Wallet, 5_000)], 5_000).Error!.Code);
        Assert.Equal("payment.reference_required", SplitPayments.Check([new(ExtraPaymentMethods.Voucher, 5_000, " ")], 5_000).Error!.Code);
        Assert.True(SplitPayments.Check([new(ExtraPaymentMethods.Wallet, 2_000, "p-1"), new(ExtraPaymentMethods.Voucher, 3_000, "ABCD-EFGH")], 5_000).IsSuccess);
    }
}

public class TipTests
{
    [Fact]
    public void A_tip_is_an_amount_or_a_percentage_of_the_subtotal_rounded_half_up_and_never_more_than_the_bill()
    {
        Assert.Equal(500, Tips.FromAmount(500, 10_000).Value);
        Assert.Equal(1_000, Tips.FromPercent(10, 10_000).Value);
        Assert.Equal(1, Tips.FromPercent(10, 5).Value);      // 0.5 rounds up
        Assert.Equal(0, Tips.FromPercent(10, 4).Value);
        Assert.Equal("tip.invalid", Tips.FromAmount(-1, 10_000).Error!.Code);
        Assert.Equal("tip.invalid", Tips.FromAmount(10_001, 10_000).Error!.Code);
        Assert.Equal("tip.invalid", Tips.FromPercent(101, 10_000).Error!.Code);
        Assert.Equal("tip.invalid", Tips.FromPercent(-5, 10_000).Error!.Code);
    }
}

public class RefundRuleTests
{
    private static readonly SoldLine Shirt = new(Guid.NewGuid(), Guid.NewGuid(), "Shirt", 3, 60_000);
    private static readonly SoldLine Socks = new(Guid.NewGuid(), Guid.NewGuid(), "Socks", 2, 10_000);

    [Fact]
    public void A_refund_gives_back_the_price_paid_for_the_quantity_returned()
    {
        var plan = RefundRules.Plan([Shirt, Socks], [], [new(Shirt.SaleLineId, 1), new(Socks.SaleLineId, 2)], 50_000).Value!;

        Assert.Equal(30_000, plan.AmountCents);
        Assert.False(plan.NeedsApproval);
        Assert.Equal([(Shirt.SaleLineId, Shirt.ProductId, 1, 20_000L), (Socks.SaleLineId, Socks.ProductId, 2, 10_000L)], plan.Lines);
    }

    [Fact]
    public void A_sale_can_never_be_refunded_for_more_than_it_was_counting_earlier_refunds()
    {
        var already = new[] { new RefundedSoFar(Shirt.SaleLineId, 2) };

        Assert.True(RefundRules.Plan([Shirt], already, [new(Shirt.SaleLineId, 1)], 50_000).IsSuccess);
        var tooMany = RefundRules.Plan([Shirt], already, [new(Shirt.SaleLineId, 2)], 50_000);
        Assert.Equal("refund.too_many", tooMany.Error!.Code);
        Assert.Contains("only 1 left", tooMany.Error.Message);
        Assert.Equal("refund.too_many", RefundRules.Plan([Shirt], [new RefundedSoFar(Shirt.SaleLineId, 3)], [new(Shirt.SaleLineId, 1)], 50_000).Error!.Code);
    }

    [Fact]
    public void A_refund_above_the_threshold_needs_a_manager()
    {
        Assert.False(RefundRules.Plan([Shirt], [], [new(Shirt.SaleLineId, 2)], 40_000).Value!.NeedsApproval);   // exactly the threshold is fine
        Assert.True(RefundRules.Plan([Shirt], [], [new(Shirt.SaleLineId, 3)], 40_000).Value!.NeedsApproval);
    }

    [Fact]
    public void The_lines_asked_for_must_be_real_positive_and_listed_once()
    {
        Assert.Equal("refund.no_lines", RefundRules.Plan([Shirt], [], [], 1).Error!.Code);
        Assert.Equal("refund.line_unknown", RefundRules.Plan([Shirt], [], [new(Guid.NewGuid(), 1)], 1).Error!.Code);
        Assert.Equal("refund.quantity_invalid", RefundRules.Plan([Shirt], [], [new(Shirt.SaleLineId, 0)], 1).Error!.Code);
        Assert.Equal("refund.line_repeated", RefundRules.Plan([Shirt], [], [new(Shirt.SaleLineId, 1), new(Shirt.SaleLineId, 1)], 1).Error!.Code);
    }

    [Fact]
    public void Money_goes_back_the_way_it_came_and_what_cannot_be_paid_back_becomes_a_voucher()
    {
        Assert.Equal("cash", RefundRules.RefundMethodFor("cash"));
        Assert.Equal("card", RefundRules.RefundMethodFor("card"));
        Assert.Equal("credit", RefundRules.RefundMethodFor("credit"));
        Assert.Equal("voucher", RefundRules.RefundMethodFor("wallet"));
        Assert.Equal("voucher", RefundRules.RefundMethodFor("layby"));
        Assert.Equal("voucher", RefundRules.RefundMethodFor("split"));
    }
}

public class WeighedSaleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Mince = Guid.NewGuid();
    private static readonly Guid Bread = Guid.NewGuid();

    private static SaleInput Sale(long total, params SaleLineInput[] lines) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, PaymentMethods.Card, total, 0, null, null, null, lines);

    [Fact]
    public void A_weighed_line_costs_the_grams_at_the_price_per_kilogram()
    {
        var line = new SaleLineInput(Mince, "Beef mince", 700, 12_500, 9_000, UnitDivisor: 1000);   // 700 g at R125/kg

        var sale = Domain.Sale.Record(Sale(10_550, line, new SaleLineInput(Bread, "Bread", 1, 1_800, 1_400)), Now);

        Assert.True(sale.IsSuccess);
        Assert.Equal(10_550, sale.Value!.TotalCents);
        Assert.Equal(8_750, sale.Value.Lines.Single(l => l.ProductId == Mince).TotalCents);
        Assert.Equal(1000, sale.Value.Lines.Single(l => l.ProductId == Mince).UnitDivisor);
    }

    [Fact]
    public void The_till_cannot_get_the_weighed_total_wrong_or_use_an_unknown_unit()
    {
        var line = new SaleLineInput(Mince, "Beef mince", 700, 12_500, 9_000, UnitDivisor: 1000);

        Assert.Equal("sale.total_mismatch", Domain.Sale.Record(Sale(8_749, line), Now).Error!.Code);
        Assert.Equal("sale.line_unit", Domain.Sale.Record(Sale(8_750, line with { UnitDivisor = 100 }), Now).Error!.Code);
    }

    [Fact]
    public void Sales_insights_and_reports_use_the_weighed_line_total_not_price_times_grams()
    {
        var row = new SaleLineRow(Guid.NewGuid(), Now, Mince, "Beef mince", 700, 12_500, 9_000, false, 1000);

        Assert.Equal((8_750L, 6_300L, 2_450L), (row.Revenue, row.Cost, row.Profit));
        Assert.Equal((1_800L, 1_400L, 400L), (new SaleLineRow(Guid.NewGuid(), Now, Bread, "Bread", 1, 1_800, 1_400, false).Revenue, new SaleLineRow(Guid.NewGuid(), Now, Bread, "Bread", 1, 1_800, 1_400, false).Cost, new SaleLineRow(Guid.NewGuid(), Now, Bread, "Bread", 1, 1_800, 1_400, false).Profit));
    }

    [Fact]
    public void A_slip_shows_weight_in_kilograms()
    {
        var text = SlipFormatter.Lines([new SlipLine("Beef mince", 700, 12_500, 1000), new SlipLine("Bread", 2, 1_800)]);

        Assert.Contains("0.700 kg x Beef mince  R87.50", text);
        Assert.Contains("2 x Bread  R36.00", text);
    }
}
