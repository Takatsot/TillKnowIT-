using ShopCore.Application.Selling;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Domain.Selling;

namespace ShopCore.Application.Tests;

public class VariantFlowTests
{
    private readonly SellingWorld _w = new();

    private Task<TillKnowIT.SharedKernel.Result<List<VariantResponse>>> Create(Guid productId, params VariantInput[] variants) =>
        _w.Variants.Handle(new CreateVariants(_w.Shop, productId, new(_w.Owner, [.. variants])), default);

    [Fact]
    public async Task Variants_are_products_of_their_own_with_their_own_barcode_price_and_cost()
    {
        var tee = _w.NewProduct("Cotton tee", 19_900, null, "clothing");

        var made = (await Create(tee.Id, new VariantInput("S / Blue", "6001234500012", 8_000, 19_900), new VariantInput("M / Blue", "6001234500029", 8_500, 21_900))).Value!;

        Assert.Equal(["Cotton tee - S / Blue", "Cotton tee - M / Blue"], made.Select(v => v.Name));
        Assert.All(_w.Store.Products.Where(p => p.ParentProductId == tee.Id), v => Assert.Equal(tee.Id, v.ParentProductId));
        Assert.Equal(3, _w.Store.Products.Count);   // the parent and two variants, and the parent is unchanged
        Assert.Equal(19_900, tee.PriceCents);
        Assert.Equal(2, (await _w.Variants.Handle(new ListVariants(_w.Shop, tee.Id, _w.Owner), default)).Value!.Count);
    }

    [Fact]
    public async Task A_pack_without_variants_or_a_person_without_access_cannot_make_them()
    {
        var tee = _w.NewProduct();

        Assert.Equal("permission.denied", (await _w.Variants.Handle(new CreateVariants(_w.Shop, tee.Id, new(Guid.NewGuid(), [new VariantInput("S", null, 0, 100)])), default)).Error!.Code);
        _w.Gate.Off.Add("variants");
        Assert.Equal("feature.not_enabled", (await Create(tee.Id, new VariantInput("S", null, 0, 100))).Error!.Code);
        Assert.Single(_w.Store.Products);
    }

    [Fact]
    public async Task Labels_and_barcodes_cannot_repeat_within_or_across_products()
    {
        var tee = _w.NewProduct();
        _w.NewProduct("Other", 1_000, "6009999999999");

        Assert.Equal("variant.label_repeated", (await Create(tee.Id, new VariantInput("S", null, 0, 100), new VariantInput(" s ", null, 0, 100))).Error!.Code);
        Assert.Equal("variant.barcode_taken", (await Create(tee.Id, new VariantInput("S", "6009999999999", 0, 100))).Error!.Code);
        Assert.Equal("variant.barcode_taken", (await Create(tee.Id, new VariantInput("S", "6001234500012", 0, 100), new VariantInput("M", "6001234500012", 0, 100))).Error!.Code);
        Assert.Equal(2, _w.Store.Products.Count);   // nothing from a failed request was kept

        await Create(tee.Id, new VariantInput("S", null, 0, 100));
        Assert.Equal("variant.label_repeated", (await Create(tee.Id, new VariantInput("S", null, 0, 100))).Error!.Code);
    }

    [Fact]
    public async Task A_variant_request_needs_a_real_product_and_a_sensible_number_of_variants()
    {
        Assert.Equal("product.not_found", (await Create(Guid.NewGuid(), new VariantInput("S", null, 0, 100))).Error!.Code);
        var tee = _w.NewProduct();
        Assert.Equal("variant.count_invalid", (await Create(tee.Id)).Error!.Code);
        Assert.Equal("variant.count_invalid", (await Create(tee.Id, [.. Enumerable.Range(0, 201).Select(i => new VariantInput($"V{i}", null, 0, 100))])).Error!.Code);
        Assert.Equal("variant.label_invalid", (await Create(tee.Id, new VariantInput(" ", null, 0, 100))).Error!.Code);
    }

    [Fact]
    public async Task Flags_turn_on_only_where_the_pack_has_the_feature_and_variants_follow_their_parent()
    {
        var tee = _w.NewProduct();
        await Create(tee.Id, new VariantInput("S", null, 0, 100));

        Assert.True((await _w.Variants.Handle(new SetSellingFlags(_w.Shop, tee.Id, new(_w.Owner, true, false, true)), default)).IsSuccess);

        Assert.Equal((true, false, true), (tee.SoldByWeight, tee.IsAddOn, tee.RequiresSerial));
        var variant = _w.Store.Products.Single(p => p.ParentProductId == tee.Id);
        Assert.Equal((true, true), (variant.SoldByWeight, variant.RequiresSerial));

        _w.Gate.Off.UnionWith(["weight", "modifiers", "serial_numbers"]);
        var other = _w.NewProduct("Other");
        Assert.Equal("feature.not_enabled", (await _w.Variants.Handle(new SetSellingFlags(_w.Shop, other.Id, new(_w.Owner, true, false, false)), default)).Error!.Code);
        Assert.Equal("feature.not_enabled", (await _w.Variants.Handle(new SetSellingFlags(_w.Shop, other.Id, new(_w.Owner, false, true, false)), default)).Error!.Code);
        Assert.Equal("feature.not_enabled", (await _w.Variants.Handle(new SetSellingFlags(_w.Shop, other.Id, new(_w.Owner, false, false, true)), default)).Error!.Code);
        Assert.True((await _w.Variants.Handle(new SetSellingFlags(_w.Shop, tee.Id, new(_w.Owner, false, false, false)), default)).IsSuccess);   // off is always allowed
        _w.Gate.Off.Clear();
        Assert.Equal("product.flags_invalid", (await _w.Variants.Handle(new SetSellingFlags(_w.Shop, other.Id, new(_w.Owner, true, true, false)), default)).Error!.Code);   // an add-on cannot be weighed
    }
}

public class ModifierFlowTests
{
    private readonly SellingWorld _w = new();
    private Product _burger = default!;
    private Product _cheese = default!;
    private Product _bacon = default!;
    private Product _mayo = default!;

    public ModifierFlowTests()
    {
        _burger = _w.NewProduct("Burger", 4_500);
        _cheese = _w.NewProduct("Cheese", 500);
        _bacon = _w.NewProduct("Bacon", 1_200);
        _mayo = _w.NewProduct("Mayo", 0);
    }

    private Task<TillKnowIT.SharedKernel.Result<ModifierGroupResponse>> Save(string name, bool required, int min, int max, params Guid[] options) =>
        _w.Modifiers.Handle(new SaveModifierGroup(_w.Shop, null, new(_w.Owner, name, required, min, max, [.. options])), default);

    private async Task<(ModifierGroupResponse Extras, ModifierGroupResponse Sauce)> Setup()
    {
        var extras = (await Save("Extras", false, 0, 2, _cheese.Id, _bacon.Id)).Value!;
        var sauce = (await Save("Sauce", true, 1, 1, _mayo.Id)).Value!;
        await _w.Modifiers.Handle(new AssignModifierGroups(_w.Shop, _burger.Id, new(_w.Owner, [extras.Id, sauce.Id])), default);
        return (extras, sauce);
    }

    [Fact]
    public async Task A_group_lists_its_options_with_their_prices_from_the_products()
    {
        var group = (await Save("Extras", false, 0, 2, _cheese.Id, _bacon.Id)).Value!;

        Assert.Equal([("Cheese", 500L), ("Bacon", 1_200L)], group.Options.Select(o => (o.Name, o.PriceCents)));
        _cheese.Update(new ProductInput(_cheese.Id, _w.Shop, _w.Cashier, _w.TillA, null, "Cheese", null, "groceries", 200, 700, 3, null, true, SellingWorld.Start.AddMinutes(1)));
        Assert.Equal(700, (await _w.Modifiers.Handle(new ListModifierGroups(_w.Shop, _w.TillA, _w.Cashier), default)).Value!.Single().Options[0].PriceCents);   // a price change shows at once
    }

    [Fact]
    public async Task Options_must_be_products_of_this_shop_and_the_limits_must_make_sense()
    {
        Assert.Equal("modifier.option_unknown", (await Save("Extras", false, 0, 1, Guid.NewGuid())).Error!.Code);
        Assert.Equal("modifier.limits_invalid", (await Save("Extras", false, 0, 3, _cheese.Id, _bacon.Id)).Error!.Code);
        Assert.Equal("modifier.name_invalid", (await Save(" ", false, 0, 1, _cheese.Id)).Error!.Code);
        _w.Gate.Off.Add("modifiers");
        Assert.Equal("feature.not_enabled", (await Save("Extras", false, 0, 1, _cheese.Id)).Error!.Code);
    }

    [Fact]
    public async Task A_group_can_be_changed_and_a_missing_one_is_not_found()
    {
        var group = (await Save("Extras", false, 0, 2, _cheese.Id, _bacon.Id)).Value!;

        var changed = await _w.Modifiers.Handle(new SaveModifierGroup(_w.Shop, group.Id, new(_w.Owner, "Toppings", true, 1, 1, [_bacon.Id])), default);

        Assert.Equal(("Toppings", true, 1), (changed.Value!.Name, changed.Value.Required, changed.Value.Options.Count));
        Assert.Equal("modifier.not_found", (await _w.Modifiers.Handle(new SaveModifierGroup(_w.Shop, Guid.NewGuid(), new(_w.Owner, "x", false, 0, 1, [_bacon.Id])), default)).Error!.Code);
    }

    [Fact]
    public async Task A_product_offers_the_groups_assigned_to_it_and_a_till_can_fetch_them()
    {
        var (extras, sauce) = await Setup();

        var offered = (await _w.Modifiers.Handle(new GetProductModifiers(_w.Shop, _burger.Id, _w.TillA, _w.Cashier), default)).Value!;

        Assert.Equal(["Extras", "Sauce"], offered.Select(g => g.Name));
        Assert.Equal("modifier.not_found", (await _w.Modifiers.Handle(new AssignModifierGroups(_w.Shop, _burger.Id, new(_w.Owner, [Guid.NewGuid()])), default)).Error!.Code);
        Assert.Equal("product.not_found", (await _w.Modifiers.Handle(new AssignModifierGroups(_w.Shop, Guid.NewGuid(), new(_w.Owner, [extras.Id])), default)).Error!.Code);
        Assert.Equal("device.not_approved", (await _w.Modifiers.Handle(new GetProductModifiers(_w.Shop, _burger.Id, Guid.NewGuid(), _w.Cashier), default)).Error!.Code);
        _ = sauce;
    }

    [Fact]
    public async Task The_server_prices_a_choice_against_the_products_groups()
    {
        await Setup();

        Task<TillKnowIT.SharedKernel.Result<long>> Price(params Guid[] chosen) =>
            _w.Modifiers.Handle(new ValidateModifiers(_w.Shop, _burger.Id, new(_w.TillA, _w.Cashier, [.. chosen])), default);

        Assert.Equal(1_700, (await Price(_cheese.Id, _bacon.Id, _mayo.Id)).Value);
        Assert.Equal("modifier.too_few", (await Price(_cheese.Id)).Error!.Code);          // the required sauce is missing
        Assert.Equal("modifier.unknown", (await Price(_mayo.Id, _burger.Id)).Error!.Code);
    }
}

public class PromotionFlowTests
{
    private readonly SellingWorld _w = new();
    private readonly Product _burger;
    private readonly Product _chips;

    public PromotionFlowTests()
    {
        _burger = _w.NewProduct("Burger", 4_500);
        _chips = _w.NewProduct("Chips", 2_000);
    }

    private PromotionRequest Percent(int pct = 20, string name = "Burger 20%", params Guid[] products) =>
        new(_w.Owner, name, "percent_off", [.. (products.Length == 0 ? [_burger.Id] : products)], pct, 0, null, SellingWorld.Start.AddDays(-1), SellingWorld.Start.AddDays(30), null, null, null);

    [Fact]
    public async Task A_promotion_is_saved_listed_for_the_till_and_applied_to_a_basket_automatically()
    {
        var saved = (await _w.Promotions.Handle(new SavePromotion(_w.Shop, null, Percent()), default)).Value!;

        Assert.Equal(("percent_off", 20, true), (saved.Type, saved.Percent, saved.IsActive));
        Assert.Single((await _w.Promotions.Handle(new ListPromotions(_w.Shop, _w.TillA, _w.Cashier), default)).Value!);

        var priced = (await _w.Promotions.Handle(new PriceBasket(_w.Shop, new(_w.TillA, _w.Cashier, [new(_burger.Id, 2, 4_500), new(_chips.Id, 1, 2_000)], null)), default)).Value!;

        Assert.Equal((11_000L, 1_800L, 9_200L), (priced.SubtotalCents, priced.DiscountCents, priced.TotalCents));
        Assert.Equal(saved.Id, Assert.Single(priced.Applied).PromotionId);
    }

    [Fact]
    public async Task A_promotion_can_be_changed_switched_off_and_then_no_longer_applies()
    {
        var saved = (await _w.Promotions.Handle(new SavePromotion(_w.Shop, null, Percent()), default)).Value!;

        var changed = await _w.Promotions.Handle(new SavePromotion(_w.Shop, saved.Id, Percent(50, "Half price")), default);
        Assert.Equal((50, "Half price"), (changed.Value!.Percent, changed.Value.Name));

        await _w.Promotions.Handle(new SetPromotionActive(_w.Shop, saved.Id, new(_w.Owner, false)), default);
        var priced = (await _w.Promotions.Handle(new PriceBasket(_w.Shop, new(_w.TillA, _w.Cashier, [new(_burger.Id, 1, 4_500)], null)), default)).Value!;

        Assert.Equal(0, priced.DiscountCents);
    }

    [Fact]
    public async Task Days_and_hours_decide_when_it_runs_in_south_african_time()
    {
        var request = Percent() with { Days = ["mon", "tue"], From = "16:00", To = "18:00" };
        await _w.Promotions.Handle(new SavePromotion(_w.Shop, null, request), default);

        Task<PricedBasketResponse> At(DateTimeOffset when) => _w.Promotions.Handle(new PriceBasket(_w.Shop, new(_w.TillA, _w.Cashier, [new(_burger.Id, 1, 4_500)], when)), default).ContinueWith(t => t.Result.Value!);

        Assert.Equal(900, (await At(new DateTimeOffset(2026, 10, 26, 17, 0, 0, TimeSpan.FromHours(2)))).DiscountCents);    // Monday 17:00
        Assert.Equal(0, (await At(new DateTimeOffset(2026, 10, 26, 19, 0, 0, TimeSpan.FromHours(2)))).DiscountCents);     // after the hours
        Assert.Equal(0, (await At(new DateTimeOffset(2026, 10, 28, 17, 0, 0, TimeSpan.FromHours(2)))).DiscountCents);     // a Wednesday
    }

    [Fact]
    public async Task A_combo_is_saved_with_its_items_and_applied()
    {
        var combo = new PromotionRequest(_w.Owner, "Burger and chips", "combo", null, 0, 5_500, [new(_burger.Id, 1), new(_chips.Id, 1)], SellingWorld.Start.AddDays(-1), SellingWorld.Start.AddDays(30), null, null, null);
        var saved = (await _w.Promotions.Handle(new SavePromotion(_w.Shop, null, combo), default)).Value!;

        var priced = (await _w.Promotions.Handle(new PriceBasket(_w.Shop, new(_w.TillA, _w.Cashier, [new(_burger.Id, 1, 4_500), new(_chips.Id, 1, 2_000)], null)), default)).Value!;

        Assert.Equal(2, saved.Items.Count);
        Assert.Equal((1_000L, 5_500L), (priced.DiscountCents, priced.TotalCents));
    }

    [Fact]
    public async Task A_promotion_is_checked_before_it_is_saved()
    {
        Task<TillKnowIT.SharedKernel.Result<PromotionResponse>> Save(PromotionRequest r) => _w.Promotions.Handle(new SavePromotion(_w.Shop, null, r), default);

        Assert.Equal("promotion.type_invalid", (await Save(Percent() with { Type = "free_lunch" })).Error!.Code);
        Assert.Equal("promotion.days_invalid", (await Save(Percent() with { Days = ["monday"] })).Error!.Code);
        Assert.Equal("promotion.hours_invalid", (await Save(Percent() with { From = "4pm", To = "6pm" })).Error!.Code);
        Assert.Equal("promotion.hours_invalid", (await Save(Percent() with { From = "16:00", To = null })).Error!.Code);
        Assert.Equal("promotion.percent_invalid", (await Save(Percent(101))).Error!.Code);
        Assert.Equal("promotion.product_unknown", (await Save(Percent(10, "x", Guid.NewGuid()))).Error!.Code);
        Assert.Equal("promotion.dates_invalid", (await Save(Percent() with { EndsAt = SellingWorld.Start.AddDays(-5) })).Error!.Code);
        Assert.Empty(_w.Store.PromotionRows);
    }

    [Fact]
    public async Task Only_the_owner_manages_promotions_a_till_reads_them_and_the_pack_switches_them_on()
    {
        Assert.Equal("permission.denied", (await _w.Promotions.Handle(new SavePromotion(_w.Shop, null, Percent() with { CallerPersonId = Guid.NewGuid() }), default)).Error!.Code);
        Assert.Equal("device.not_approved", (await _w.Promotions.Handle(new ListPromotions(_w.Shop, Guid.NewGuid(), _w.Cashier), default)).Error!.Code);
        Assert.Equal("promotion.not_found", (await _w.Promotions.Handle(new SavePromotion(_w.Shop, Guid.NewGuid(), Percent()), default)).Error!.Code);

        _w.Gate.Off.Add("promotions");
        Assert.Equal("feature.not_enabled", (await _w.Promotions.Handle(new SavePromotion(_w.Shop, null, Percent()), default)).Error!.Code);
        Assert.Equal("feature.not_enabled", (await _w.Promotions.Handle(new ListPromotions(_w.Shop, _w.TillA, _w.Cashier), default)).Error!.Code);
    }

    [Fact]
    public async Task Pricing_a_basket_works_without_the_feature_and_just_gives_no_discount_and_bad_baskets_are_refused()
    {
        await _w.Promotions.Handle(new SavePromotion(_w.Shop, null, Percent()), default);
        _w.Gate.Off.Add("promotions");

        var priced = (await _w.Promotions.Handle(new PriceBasket(_w.Shop, new(_w.TillA, _w.Cashier, [new(_burger.Id, 1, 4_500)], null)), default)).Value!;

        Assert.Equal((4_500L, 0L), (priced.TotalCents, priced.DiscountCents));
        Assert.Equal("basket.invalid", (await _w.Promotions.Handle(new PriceBasket(_w.Shop, new(_w.TillA, _w.Cashier, [], null)), default)).Error!.Code);
        Assert.Equal("basket.invalid", (await _w.Promotions.Handle(new PriceBasket(_w.Shop, new(_w.TillA, _w.Cashier, [new(_burger.Id, 0, 100)], null)), default)).Error!.Code);
    }
}

public class FloorPlanFlowTests
{
    private readonly SellingWorld _w = new();

    private Task<TillKnowIT.SharedKernel.Result<List<FloorTableResponse>>> Save(params FloorTableDto[] tables) =>
        _w.Floor.Handle(new SaveFloorPlan(_w.Shop, new(_w.Owner, [.. tables])), default);

    [Fact]
    public async Task The_owner_sets_the_plan_and_a_till_reads_it_in_order()
    {
        await Save(new FloorTableDto("Inside", "T1", 4), new FloorTableDto("Inside", "T2", 2), new FloorTableDto("Patio", "T1", 6));

        var plan = (await _w.Floor.Handle(new GetFloorPlan(_w.Shop, _w.TillA, _w.Cashier), default)).Value!;

        Assert.Equal([("Inside", "T1"), ("Inside", "T2"), ("Patio", "T1")], plan.Select(t => (t.Area, t.Name)));
    }

    [Fact]
    public async Task Saving_again_keeps_the_ids_of_tables_that_stay_and_retires_the_ones_that_go()
    {
        var first = (await Save(new FloorTableDto("Inside", "T1", 4), new FloorTableDto("Inside", "T2", 2))).Value!;

        var second = (await Save(new FloorTableDto("inside", "t2", 8), new FloorTableDto("Patio", "P1", 4))).Value!;

        Assert.Equal(first.Single(t => t.Name == "T2").Id, second.Single(t => t.Name == "T2").Id);
        Assert.Equal(8, second.Single(t => t.Name == "T2").Seats);
        Assert.DoesNotContain(second, t => t.Name == "T1");
        Assert.Equal(3, _w.Store.TableRows.Count);   // T1 is retired, not deleted
        Assert.False(_w.Store.TableRows.Single(t => t.Name == "T1").IsActive);

        var third = (await Save(new FloorTableDto("Inside", "T1", 4))).Value!;   // bring it back: same table
        Assert.Equal(first.Single(t => t.Name == "T1").Id, third.Single().Id);
    }

    [Fact]
    public async Task A_bad_plan_the_wrong_person_or_a_pack_without_tables_changes_nothing()
    {
        Assert.Equal("floor.duplicate", (await Save(new FloorTableDto("Inside", "T1", 4), new FloorTableDto("Inside", "T1", 2))).Error!.Code);
        Assert.Equal("floor.seats_invalid", (await Save(new FloorTableDto("Inside", "T1", 0))).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Floor.Handle(new SaveFloorPlan(_w.Shop, new(Guid.NewGuid(), [])), default)).Error!.Code);
        _w.Gate.Off.Add("tables");
        Assert.Equal("feature.not_enabled", (await Save(new FloorTableDto("Inside", "T1", 4))).Error!.Code);
        Assert.Equal("feature.not_enabled", (await _w.Floor.Handle(new GetFloorPlan(_w.Shop, _w.TillA, _w.Cashier), default)).Error!.Code);
        Assert.Empty(_w.Store.TableRows);
    }
}
