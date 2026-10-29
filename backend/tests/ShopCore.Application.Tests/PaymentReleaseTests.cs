using ShopCore.Domain.Selling;

namespace ShopCore.Application.Tests;

public class PaymentReleaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static GiftVoucher Voucher() => GiftVoucher.Issue(Guid.NewGuid(), 10_000, 12, Guid.NewGuid(), Guid.NewGuid(), null, "issue-1", Now).Value.Voucher;

    [Fact]
    public void A_redemption_for_a_sale_that_was_not_completed_is_given_back_once()
    {
        var v = Voucher();
        var sale = Guid.NewGuid();
        v.Redeem(4_000, sale, "r1", Guid.NewGuid(), Now);
        Assert.Equal(6_000, v.BalanceCents);

        Assert.True(v.Release("r1", Guid.NewGuid(), Now).IsSuccess);
        Assert.True(v.Release("r1", Guid.NewGuid(), Now).IsSuccess);   // again: nothing more

        Assert.Equal(10_000, v.BalanceCents);
        Assert.Equal(sale, v.SaleOfRedemption("r1"));
    }

    [Fact]
    public void A_redemption_that_never_happened_cannot_be_released()
    {
        Assert.Equal("voucher.redemption_not_found", Voucher().Release("nope", Guid.NewGuid(), Now).Error!.Code);
    }

    [Fact]
    public void Points_can_be_one_part_of_a_split_alongside_a_voucher()
    {
        var parts = new List<PaymentPart> { new("voucher", 2_000, "v-1"), new("points", 1_000, "points:abc"), new("cash", 7_000) };

        Assert.True(SplitPayments.Check(parts, 10_000).IsSuccess);
        Assert.Equal("payment.reference_required", SplitPayments.Check([new("points", 10_000)], 10_000).Error!.Code);
    }
}
