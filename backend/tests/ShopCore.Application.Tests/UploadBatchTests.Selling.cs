using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public partial class UploadBatchTests
{
    private sealed class FakeRefundStore : IRefundStore
    {
        public HashSet<Guid> Existing { get; } = [];
        public Dictionary<Guid, List<SoldLine>> Lines { get; } = [];
        public List<(Guid SaleId, Guid ProductId, int Quantity)> Refunded { get; } = [];
        public List<SaleRefund> Added { get; } = [];
        public long StoredCashRefunds { get; set; }

        public Task<HashSet<Guid>> ExistingIds(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(ids.Where(Existing.Contains).ToHashSet());
        public Task<Dictionary<Guid, List<SoldLine>>> SoldLines(Guid shopId, IEnumerable<Guid> saleIds, CancellationToken ct) =>
            Task.FromResult(saleIds.Where(Lines.ContainsKey).ToDictionary(i => i, i => Lines[i]));
        public Task<List<(Guid SaleId, Guid ProductId, int Quantity)>> RefundedSoFar(IEnumerable<Guid> saleIds, CancellationToken ct) =>
            Task.FromResult(Refunded.Where(r => saleIds.Contains(r.SaleId)).ToList());
        public Task<long> CashRefunds(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult(StoredCashRefunds);
        public long StoredCashIn { get; set; }
        public Task<long> CashIn(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult(StoredCashIn);
        public void Add(SaleRefund refund) => Added.Add(refund);
    }

    private sealed class FakeGate : IFeatureGate
    {
        public HashSet<string> Off { get; } = [];
        public string? RefundSettings { get; set; }
        public Task<bool> IsOn(Guid shopId, string feature, CancellationToken ct) => Task.FromResult(!Off.Contains(feature));
        public Task<string?> Settings(Guid shopId, string feature, CancellationToken ct) => Task.FromResult(feature == "refunds" ? RefundSettings : null);
    }

    private readonly FakeRefundStore _refunds = new();
    private readonly FakeGate _gate = new();

    private Task<Result<SyncUploadResponse>> UploadSelling(
        List<SaleUpload>? sales = null, List<SaleCancellationUpload>? cancels = null, List<CashUpUpload>? cashUps = null, List<RefundUpload>? refunds = null,
        List<StockMovementUpload>? moves = null) =>
        new UploadBatchHandler(_store, _access, new FakeLookups(), new FakeTimeProvider(Now), _refunds, _gate)
            .Handle(new UploadBatch(new SyncUploadRequest(_shop, _device, sales, moves, cancels, null, null, null, cashUps, refunds)), default);

    private SaleUpload SplitSale(Guid? id = null, long total = 3_600, params PaymentPartUpload[] parts) => new(
        id ?? Guid.NewGuid(), _cashier, Now, "split", total, 0, null, null, null, [new SaleLineUpload(_bread, "Bread", 2, 1_800, 1_400)],
        parts.Length > 0 ? [.. parts] : [new("cash", 2_000, null, 2_500), new("card", 1_600)]);

    private CashUpUpload CashUp(long floatCents, long expected, long counted) =>
        new(Guid.NewGuid(), _cashier, new DateOnly(2026, 10, 26), floatCents, expected, counted, null, Now.AddHours(1));

    // ---- split payments ----

    [Fact]
    public async Task A_split_sale_is_recorded_with_its_parts_and_works_out_the_cash_change()
    {
        var result = await UploadSelling([SplitSale()]);

        Assert.Equal("accepted", result.Value!.Results.Single().Status);
        var sale = _store.AddedSales.Single();
        Assert.Equal(["cash", "card"], sale.Payments.Select(p => p.MethodCode));
        Assert.Equal((2_500L, 500L), (sale.TenderedCents!.Value, sale.ChangeCents!.Value));
        Assert.Equal(2_000, sale.CashCents);
    }

    [Fact]
    public async Task A_split_sale_whose_parts_do_not_add_up_or_include_credit_or_have_no_parts_is_rejected()
    {
        var results = (await UploadSelling([
            SplitSale(null, 3_600, new PaymentPartUpload("cash", 2_000), new PaymentPartUpload("card", 1_500)),
            SplitSale(null, 3_600, new PaymentPartUpload("cash", 2_000), new PaymentPartUpload("credit", 1_600)),
            CashSale() with { Payments = [new("cash", 3_600, null, 5_000)], TenderedCents = 5_000, ChangeCents = 1_400 },
            SplitSale() with { Payments = null },
        ])).Value!.Results;

        Assert.Equal(["payment.total_mismatch", "sale.split_credit", "sale.payment_method", "sale.payment_parts_required"], results.Select(r => r.Code));
        Assert.Empty(_store.AddedSales);
    }

    [Fact]
    public async Task A_tip_is_on_top_of_the_bill_in_the_drawer_and_not_in_the_sale_total()
    {
        var sale = CashSale() with { TipCents = 400, TenderedCents = 5_000, ChangeCents = 1_000 };   // 3600 + 400 tip, handed R50, change R10

        var result = await UploadSelling([sale]);

        Assert.Equal("accepted", result.Value!.Results.Single().Status);
        Assert.Equal((3_600L, 400L, 4_000L), (_store.AddedSales.Single().TotalCents, _store.AddedSales.Single().TipCents, _store.AddedSales.Single().CashCents));
        Assert.Equal("sale.tender_invalid", (await UploadSelling([CashSale() with { TipCents = 400, TenderedCents = 5_000, ChangeCents = 1_400 }])).Value!.Results.Single().Code);   // change ignored the tip
        Assert.Equal("sale.tip_invalid", (await UploadSelling([CashSale() with { TipCents = 3_601, TenderedCents = 9_000, ChangeCents = 1_799 }])).Value!.Results.Single().Code);
    }

    [Fact]
    public async Task The_cash_up_counts_the_cash_part_of_a_split_sale_and_the_tip_but_not_the_card_part()
    {
        var split = SplitSale();                                                                                      // 2000 cash
        var tipped = CashSale() with { TipCents = 400, TenderedCents = 4_000, ChangeCents = 0 };                      // 4000 cash

        var result = await UploadSelling([split, tipped], cashUps: [CashUp(10_000, 16_000, 16_000)]);

        Assert.Equal("accepted", result.Value!.Results.Last().Status);
        Assert.Equal(16_000, _store.AddedCashUps.Single().ExpectedCents);   // float 10000 + 2000 + 4000
        Assert.Null(result.Value.Results.Last().Code);
    }

    [Fact]
    public async Task Cash_refunds_come_out_of_the_expected_cash()
    {
        _store.Sales.Add(Guid.NewGuid());
        var sale = CashSale();
        await UploadSelling([sale]);
        _store.Sales.Add(sale.Id);
        _store.Summaries[sale.Id] = new SaleSummary("cash", null, 3_600);
        _refunds.Lines[sale.Id] = [new SoldLine(_bread, _bread, "Bread", 2, 3_600)];
        _store.AddedSales.Clear();
        _refunds.StoredCashRefunds = 1_800;   // an earlier refund of one loaf
        _store.StoredCashSales.Add(new CashSaleRow(sale.Id, 3_600, false));

        var result = await UploadSelling(cashUps: [CashUp(10_000, 11_800, 11_800)]);

        Assert.Equal(11_800, _store.AddedCashUps.Single().ExpectedCents);   // 10000 + 3600 - 1800
        Assert.Equal("accepted", result.Value!.Results.Single().Status);
    }

    // ---- features ----

    [Fact]
    public async Task Split_payments_tips_customers_promotions_and_weight_need_their_feature_switched_on()
    {
        _gate.Off.UnionWith(["split_payments", "tips", "customers", "promotions", "weight"]);
        var promoLine = new SaleLineUpload(_bread, "Bread", 2, 1_800, 1_400, DiscountCents: 360, PromotionId: Guid.NewGuid());
        var weighed = new SaleLineUpload(_bread, "Mince", 700, 12_500, 9_000, UnitDivisor: 1000);

        var results = (await UploadSelling([
            SplitSale(),
            CashSale() with { TipCents = 100, TenderedCents = 3_700, ChangeCents = 0 },
            CashSale() with { CustomerId = Guid.NewGuid() },
            CashSale() with { Lines = [promoLine], TotalCents = 3_240, TenderedCents = 3_240, ChangeCents = 0 },
            CashSale() with { Lines = [weighed], TotalCents = 8_750, TenderedCents = 8_750, ChangeCents = 0 },
        ])).Value!.Results;

        Assert.All(results, r => Assert.Equal("feature.not_enabled", r.Code));
        Assert.Contains("split_payments", results[0].Message);
        Assert.Contains("weight", results[4].Message);
        Assert.Empty(_store.AddedSales);
    }

    [Fact]
    public async Task With_the_features_on_the_same_sales_go_through()
    {
        var promoLine = new SaleLineUpload(_bread, "Bread", 2, 1_800, 1_400, DiscountCents: 360, PromotionId: Guid.NewGuid());

        var results = (await UploadSelling([SplitSale(), CashSale() with { Lines = [promoLine], TotalCents = 3_240, TenderedCents = 3_240, ChangeCents = 0 }])).Value!.Results;

        Assert.All(results, r => Assert.Equal("accepted", r.Status));
        Assert.Equal(3_240, _store.AddedSales.Last().TotalCents);
        Assert.Equal(360, _store.AddedSales.Last().Lines.Single().DiscountCents);
    }

    // ---- discounts ----

    [Fact]
    public async Task A_discount_needs_a_reason_and_cannot_be_more_than_the_item_costs()
    {
        SaleUpload WithDiscount(long off, Guid? promotion = null, Guid? manager = null) => CashSale() with
        {
            Lines = [new SaleLineUpload(_bread, "Bread", 2, 1_800, 1_400, DiscountCents: off, PromotionId: promotion, PriceOverrideBy: manager)],
            TotalCents = 3_600 - off, TenderedCents = 3_600 - off, ChangeCents = 0,
        };

        var results = (await UploadSelling([WithDiscount(500), WithDiscount(3_601, Guid.NewGuid()), WithDiscount(-1, Guid.NewGuid()), WithDiscount(500, null, _manager)])).Value!.Results;

        Assert.Equal(["sale.discount_unexplained", "sale.discount_invalid", "sale.discount_invalid", null], results.Select(r => r.Code));
        Assert.Equal("accepted", results[3].Status);
    }

    [Fact]
    public async Task A_price_override_must_come_from_someone_whose_role_allows_it()
    {
        _access.Denied[(_manager, "sale.price_override")] = Result.Fail("permission.denied", "No.");
        var sale = CashSale() with
        {
            Lines = [new SaleLineUpload(_bread, "Bread", 2, 1_800, 1_400, DiscountCents: 500, PriceOverrideBy: _manager)],
            TotalCents = 3_100, TenderedCents = 3_100, ChangeCents = 0,
        };

        var result = await UploadSelling([sale]);

        Assert.Equal("sale.override_not_allowed", result.Value!.Results.Single().Code);
    }

    // ---- refunds ----

    private SaleUpload Prior(string method = "cash", Guid? account = null, long total = 3_600)
    {
        var sale = CashSale();
        _store.Sales.Add(sale.Id);
        _store.Summaries[sale.Id] = new SaleSummary(method, account, total);
        _refunds.Lines[sale.Id] = [new SoldLine(_bread, _bread, "Bread", 2, total)];
        return sale;
    }

    private RefundUpload Refund(Guid saleId, int qty = 1, string? method = null, Guid? approvedBy = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), _cashier, saleId, "Wrong size", method, approvedBy, [new RefundLineUpload(_bread, qty)], Now);

    [Fact]
    public async Task A_refund_puts_the_goods_back_in_stock_and_records_how_the_customer_was_paid_back()
    {
        var sale = Prior();

        var result = await UploadSelling(refunds: [Refund(sale.Id)]);

        Assert.Equal("accepted", result.Value!.Results.Single().Status);
        var refund = _refunds.Added.Single();
        Assert.Equal((1_800L, "cash", sale.Id), (refund.AmountCents, refund.MethodCode, refund.SaleId));
        var movement = _store.AddedMovements.Single();
        Assert.Equal((StockMovementType.Return, 1, sale.Id), (movement.Type, movement.Quantity, movement.SaleId));
    }

    [Fact]
    public async Task A_refund_can_be_for_a_sale_in_the_same_batch()
    {
        var sale = CashSale();

        var result = await UploadSelling([sale], refunds: [Refund(sale.Id, 2)]);

        Assert.All(result.Value!.Results, r => Assert.Equal("accepted", r.Status));
        Assert.Equal(3_600, _refunds.Added.Single().AmountCents);
    }

    [Fact]
    public async Task Refunds_in_one_batch_cannot_together_take_back_more_than_was_sold()
    {
        var sale = Prior();

        var results = (await UploadSelling(refunds: [Refund(sale.Id, 1), Refund(sale.Id, 1), Refund(sale.Id, 1)])).Value!.Results;

        Assert.Equal(["accepted", "accepted", "refund.too_many"], results.Select(r => r.Code ?? "accepted"));
    }

    [Fact]
    public async Task Earlier_refunds_count_and_a_refund_sent_twice_is_a_duplicate()
    {
        var sale = Prior();
        _refunds.Refunded.Add((sale.Id, _bread, 2));
        var refund = Refund(sale.Id);

        Assert.Equal("refund.too_many", (await UploadSelling(refunds: [refund])).Value!.Results.Single().Code);

        _refunds.Refunded.Clear();
        _refunds.Existing.Add(refund.Id);
        Assert.Equal("duplicate", (await UploadSelling(refunds: [refund])).Value!.Results.Single().Status);
        Assert.Empty(_refunds.Added);
    }

    [Fact]
    public async Task A_big_refund_needs_a_manager_who_may_approve_it()
    {
        var sale = Prior(total: 80_000);
        _refunds.Lines[sale.Id] = [new SoldLine(_bread, _bread, "Bread", 2, 80_000)];

        var none = (await UploadSelling(refunds: [Refund(sale.Id, 2)])).Value!.Results.Single();
        _access.Denied[(_manager, "sale.refund_approve")] = Result.Fail("permission.denied", "No.");
        var notAllowed = (await UploadSelling(refunds: [Refund(sale.Id, 2, null, _manager)])).Value!.Results.Single();
        _access.Denied.Clear();
        var approved = (await UploadSelling(refunds: [Refund(sale.Id, 2, null, _manager)])).Value!.Results.Single();

        Assert.Equal(("refund.approval_required", "refund.approver_not_allowed", "accepted"), (none.Code, notAllowed.Code, approved.Status));
        Assert.Equal(_manager, _refunds.Added.Single().ApprovedBy);
    }

    [Fact]
    public async Task The_shop_can_set_its_own_approval_threshold()
    {
        var sale = Prior();
        _gate.RefundSettings = "{\"approvalThresholdCents\":1000}";

        Assert.Equal("refund.approval_required", (await UploadSelling(refunds: [Refund(sale.Id)])).Value!.Results.Single().Code);   // R18 is over R10

        _gate.RefundSettings = "{\"approvalThresholdCents\":2000}";
        Assert.Equal("accepted", (await UploadSelling(refunds: [Refund(sale.Id)])).Value!.Results.Single().Status);
    }

    [Fact]
    public async Task Only_staff_who_may_refund_can_and_the_sale_must_exist_and_not_be_cancelled()
    {
        var sale = Prior();
        _access.Denied[(_cashier, "sale.refund")] = Result.Fail("permission.denied", "No.");
        Assert.Equal("permission.denied", (await UploadSelling(refunds: [Refund(sale.Id)])).Value!.Results.Single().Code);

        _access.Denied.Clear();
        Assert.Equal("refund.sale_unknown", (await UploadSelling(refunds: [Refund(Guid.NewGuid())])).Value!.Results.Single().Code);

        _store.CancelledSales.Add(sale.Id);
        Assert.Equal("refund.sale_cancelled", (await UploadSelling(refunds: [Refund(sale.Id)])).Value!.Results.Single().Code);
    }

    [Fact]
    public async Task A_refund_needs_a_reason_and_a_sensible_quantity_and_a_known_item()
    {
        var sale = Prior();

        Assert.Equal("refund.reason_required", (await UploadSelling(refunds: [Refund(sale.Id) with { Reason = " " }])).Value!.Results.Single().Code);
        Assert.Equal("refund.quantity_invalid", (await UploadSelling(refunds: [Refund(sale.Id, 0)])).Value!.Results.Single().Code);
        Assert.Equal("refund.line_unknown", (await UploadSelling(refunds: [Refund(sale.Id) with { Lines = [new RefundLineUpload(Guid.NewGuid(), 1)] }])).Value!.Results.Single().Code);
        Assert.Equal("refund.no_lines", (await UploadSelling(refunds: [Refund(sale.Id) with { Lines = [] }])).Value!.Results.Single().Code);
    }

    [Fact]
    public async Task A_credit_sale_is_refunded_by_reducing_what_the_customer_owes()
    {
        var account = Guid.NewGuid();
        var sale = Prior("credit", account);

        var result = await UploadSelling(refunds: [Refund(sale.Id)]);

        Assert.Equal("accepted", result.Value!.Results.Single().Status);
        var entry = _store.AddedCreditEntries.Single();
        Assert.Equal((CreditEntryType.Refund, -1_800L, account, sale.Id), (entry.Type, entry.AmountCents, entry.AccountId, entry.SaleId));
        Assert.Equal("refund.method_invalid", (await UploadSelling(refunds: [Refund(sale.Id, 1, "cash")])).Value!.Results.Single().Code);
    }

    [Fact]
    public async Task A_cash_sale_cannot_be_refunded_to_a_credit_account_and_a_wallet_or_voucher_sale_is_refunded_online()
    {
        var cash = Prior();
        var wallet = Prior("wallet");

        Assert.Equal("refund.method_invalid", (await UploadSelling(refunds: [Refund(cash.Id, 1, "credit")])).Value!.Results.Single().Code);
        Assert.Equal("refund.online_only", (await UploadSelling(refunds: [Refund(wallet.Id)])).Value!.Results.Single().Code);
        Assert.Equal("refund.online_only", (await UploadSelling(refunds: [Refund(cash.Id, 1, "voucher")])).Value!.Results.Single().Code);
        Assert.Equal("refund.method_invalid", (await UploadSelling(refunds: [Refund(cash.Id, 1, "barter")])).Value!.Results.Single().Code);
    }

    [Fact]
    public async Task A_card_sale_can_be_refunded_to_the_card_and_refunds_need_the_feature()
    {
        var sale = Prior("card");

        Assert.Equal("accepted", (await UploadSelling(refunds: [Refund(sale.Id)])).Value!.Results.Single().Status);
        Assert.Equal("card", _refunds.Added.Single().MethodCode);

        _gate.Off.Add("refunds");
        Assert.Equal("feature.not_enabled", (await UploadSelling(refunds: [Refund(sale.Id)])).Value!.Results.Single().Code);
    }

    [Fact]
    public async Task A_discounted_line_is_refunded_for_what_was_actually_paid()
    {
        var sale = CashSale();
        _store.Sales.Add(sale.Id);
        _store.Summaries[sale.Id] = new SaleSummary("cash", null, 3_240);
        _refunds.Lines[sale.Id] = [new SoldLine(_bread, _bread, "Bread", 2, 3_240)];   // R36.00 less R3.60 promotion

        await UploadSelling(refunds: [Refund(sale.Id, 1), Refund(sale.Id, 1)]);

        Assert.Equal([1_620L, 1_620L], _refunds.Added.Select(r => r.AmountCents));
        Assert.Equal(3_240, _refunds.Added.Sum(r => r.AmountCents));   // together exactly what was paid
    }
}
