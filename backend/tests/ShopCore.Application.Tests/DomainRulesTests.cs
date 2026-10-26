using ShopCore.Domain;

namespace ShopCore.Application.Tests;

public class DomainRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Bread = Guid.NewGuid();
    private static readonly Guid Milk = Guid.NewGuid();

    private static SaleInput Cash(long tendered = 10000, params SaleLineInput[] lines) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, PaymentMethods.Cash,
        TotalCents: (lines.Length == 0 ? Default : lines).Sum(l => l.PriceCents * l.Quantity), VatCents: 0,
        TenderedCents: tendered, ChangeCents: tendered - (lines.Length == 0 ? Default : lines).Sum(l => l.PriceCents * l.Quantity),
        AccountId: null, Lines: lines.Length == 0 ? Default : lines);

    private static readonly SaleLineInput[] Default =
    [
        new(Bread, "Bread", 2, 1800, 1400),
        new(Milk, "Milk 1l", 1, 2500, 2100),
    ];

    // ---- Sale ----

    [Fact]
    public void Valid_cash_sale_keeps_the_ids_and_times_the_till_gave()
    {
        var input = Cash();
        var result = Sale.Record(input, Now);

        Assert.True(result.IsSuccess);
        var sale = result.Value!;
        Assert.Equal(input.Id, sale.Id);
        Assert.Equal(6100, sale.TotalCents);
        Assert.Equal(Now, sale.SoldAt);
        Assert.Equal(input.StaffId, sale.CreatedBy);
        Assert.Equal(input.TillDeviceId, sale.DeviceId);
        Assert.All(sale.Lines, l => Assert.Equal(sale.Id, l.SaleId));
    }

    [Fact]
    public void Total_is_recomputed_so_a_tampered_total_is_rejected()
    {
        var input = Cash() with { TotalCents = 100, ChangeCents = 4900 };
        Assert.Equal("sale.total_mismatch", Sale.Record(input, Now).Error!.Code);
    }

    [Fact]
    public void Cash_must_cover_the_total_and_change_must_add_up()
    {
        Assert.Equal("sale.tender_invalid", Sale.Record(Cash(tendered: 6000) with { TenderedCents = 6000, ChangeCents = 0 }, Now).Error!.Code);
        Assert.Equal("sale.tender_invalid", Sale.Record(Cash() with { ChangeCents = 1 }, Now).Error!.Code);
        Assert.Equal("sale.tender_invalid", Sale.Record(Cash() with { TenderedCents = null, ChangeCents = null }, Now).Error!.Code);
    }

    [Fact]
    public void Card_sale_has_no_tendered_and_credit_needs_an_account()
    {
        var card = Cash() with { PaymentMethodCode = PaymentMethods.Card, TenderedCents = null, ChangeCents = null };
        Assert.True(Sale.Record(card, Now).IsSuccess);
        Assert.Equal("sale.tender_invalid", Sale.Record(card with { TenderedCents = 100 }, Now).Error!.Code);

        var credit = card with { PaymentMethodCode = PaymentMethods.Credit };
        Assert.Equal("sale.account_required", Sale.Record(credit, Now).Error!.Code);
        Assert.True(Sale.Record(credit with { AccountId = Guid.NewGuid() }, Now).IsSuccess);
        Assert.Equal("sale.account_unexpected", Sale.Record(card with { AccountId = Guid.NewGuid() }, Now).Error!.Code);
    }

    [Theory]
    [InlineData("cheque")]
    [InlineData("")]
    public void Unknown_payment_method_is_rejected(string method) =>
        Assert.Equal("sale.payment_method", Sale.Record(Cash() with { PaymentMethodCode = method }, Now).Error!.Code);

    [Fact]
    public void Empty_sale_and_bad_lines_are_rejected()
    {
        Assert.Equal("sale.no_lines", Sale.Record(Cash() with { Lines = [], TotalCents = 0, TenderedCents = 0, ChangeCents = 0 }, Now).Error!.Code);
        Assert.Equal("sale.line_quantity", Sale.Record(Cash(lines: new SaleLineInput(Bread, "Bread", 0, 1800, 1400)), Now).Error!.Code);
        Assert.Equal("sale.line_price", Sale.Record(Cash(lines: new SaleLineInput(Bread, "Bread", 1, -5, 0)), Now).Error!.Code);
        Assert.Equal("sale.line_name", Sale.Record(Cash(lines: new SaleLineInput(Bread, " ", 1, 100, 0)), Now).Error!.Code);
    }

    [Fact]
    public void Same_product_twice_must_be_merged_by_the_till()
    {
        var input = Cash(lines: [new SaleLineInput(Bread, "Bread", 1, 1800, 1400), new SaleLineInput(Bread, "Bread", 1, 1800, 1400)]);
        Assert.Equal("sale.duplicate_line", Sale.Record(input, Now).Error!.Code);
    }

    [Fact]
    public void Vat_must_be_within_the_total_and_future_dates_are_rejected()
    {
        Assert.Equal("sale.vat_invalid", Sale.Record(Cash() with { VatCents = 99999 }, Now).Error!.Code);
        Assert.Equal("sale.vat_invalid", Sale.Record(Cash() with { VatCents = -1 }, Now).Error!.Code);
        Assert.Equal("sale.date_invalid", Sale.Record(Cash() with { SoldAt = Now.AddDays(2) }, Now).Error!.Code);
        Assert.True(Sale.Record(Cash() with { SoldAt = Now.AddHours(3) }, Now).IsSuccess);
    }

    // ---- Stock movements ----

    private static StockMovementInput Move(StockMovementType type, int qty, Guid? saleId = null, string? reason = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Bread, type, qty, Guid.NewGuid(), Guid.NewGuid(), Now, saleId, null, reason, null, null);

    [Theory]
    [InlineData(StockMovementType.Receive, 5, true)]
    [InlineData(StockMovementType.Receive, -5, false)]
    [InlineData(StockMovementType.WriteOff, -2, true)]
    [InlineData(StockMovementType.WriteOff, 2, false)]
    [InlineData(StockMovementType.Count, 3, true)]
    [InlineData(StockMovementType.Count, -3, true)]
    public void Movement_signs_follow_the_type(StockMovementType type, int qty, bool ok) =>
        Assert.Equal(ok, StockMovement.Record(Move(type, qty, reason: "counted")).IsSuccess);

    [Fact]
    public void Sale_and_cancel_movements_point_at_their_sale_and_have_the_right_sign()
    {
        var sale = Guid.NewGuid();
        Assert.True(StockMovement.Record(Move(StockMovementType.Sale, -2, sale)).IsSuccess);
        Assert.True(StockMovement.Record(Move(StockMovementType.Cancel, 2, sale)).IsSuccess);
        Assert.Equal("stock.sale_required", StockMovement.Record(Move(StockMovementType.Sale, -2)).Error!.Code);
        Assert.Equal("stock.quantity_sign", StockMovement.Record(Move(StockMovementType.Cancel, -2, sale)).Error!.Code);
    }

    [Fact]
    public void Counts_and_write_offs_need_a_reason_and_zero_is_never_allowed()
    {
        Assert.Equal("stock.reason_required", StockMovement.Record(Move(StockMovementType.Count, 1)).Error!.Code);
        Assert.Equal("stock.reason_required", StockMovement.Record(Move(StockMovementType.WriteOff, -1, reason: " ")).Error!.Code);
        Assert.Equal("stock.quantity_zero", StockMovement.Record(Move(StockMovementType.Receive, 0)).Error!.Code);
    }

    [Fact]
    public void Stock_on_hand_is_the_sum_and_can_go_negative()
    {
        var onHand = StockCalculator.OnHand([(Bread, 10), (Bread, -4), (Bread, 1), (Milk, -2)]);

        Assert.Equal(7, onHand[Bread]);
        Assert.Equal(-2, onHand[Milk]);
        Assert.False(StockCalculator.NeedsCount(onHand[Bread]));
        Assert.True(StockCalculator.NeedsCount(onHand[Milk]));
    }

    // ---- Cancellation ----

    [Fact]
    public void Cancellation_needs_a_reason()
    {
        Assert.Equal("cancellation.reason_required",
            SaleCancellation.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " ", Now).Error!.Code);
        Assert.True(SaleCancellation.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Wrong item", Now).IsSuccess);
    }
}
