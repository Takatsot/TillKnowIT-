using Payments.Application;
using Payments.Domain;

namespace Payments.Application.Tests;

public class WebhookSignatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private const string Secret = "shh";
    private const string Body = "{\"id\":\"e1\",\"type\":\"payment.held\"}";

    [Fact]
    public void A_correctly_signed_recent_callback_is_accepted()
    {
        var header = WebhookSignature.Sign(Secret, Now, Body);
        Assert.True(WebhookSignature.Verify(Secret, header, Body, Now.AddMinutes(1)));
    }

    [Fact]
    public void A_changed_body_a_wrong_secret_or_a_missing_or_garbled_header_is_refused()
    {
        var header = WebhookSignature.Sign(Secret, Now, Body);

        Assert.False(WebhookSignature.Verify(Secret, header, Body + " ", Now));
        Assert.False(WebhookSignature.Verify("other", header, Body, Now));
        Assert.False(WebhookSignature.Verify(Secret, null, Body, Now));
        Assert.False(WebhookSignature.Verify(Secret, "", Body, Now));
        Assert.False(WebhookSignature.Verify(Secret, "v1=abc", Body, Now));
        Assert.False(WebhookSignature.Verify(Secret, "t=notanumber,v1=abc", Body, Now));
        Assert.False(WebhookSignature.Verify("", header, Body, Now));
    }

    [Fact]
    public void An_old_or_future_callback_is_refused_so_a_captured_one_cannot_be_replayed()
    {
        var header = WebhookSignature.Sign(Secret, Now, Body);

        Assert.True(WebhookSignature.Verify(Secret, header, Body, Now.AddMinutes(4)));
        Assert.False(WebhookSignature.Verify(Secret, header, Body, Now.AddMinutes(6)));
        Assert.False(WebhookSignature.Verify(Secret, header, Body, Now.AddMinutes(-6)));
    }
}

public class PaymentDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Person = Guid.NewGuid();

    private static Payment Held(long amount = 100_000)
    {
        var p = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, Person, Now).Value!;
        p.SetCheckout("ref_1", "https://pay/1");
        p.MarkHeld(amount, Now);
        return p;
    }

    [Fact]
    public void A_payment_needs_an_amount()
    {
        Assert.Equal("payment.amount_invalid", Payment.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Person, Now).Error!.Code);
    }

    [Fact]
    public void The_partner_confirming_the_exact_amount_holds_the_money_and_a_repeat_changes_nothing()
    {
        var p = Held();
        var version = p.Version;

        Assert.True(p.MarkHeld(100_000, Now).IsSuccess);

        Assert.Equal((PaymentStatus.Held, version), (p.Status, p.Version));
        Assert.Equal(2, p.Audit.Count);
    }

    [Fact]
    public void A_different_amount_from_the_partner_fails_the_payment_and_says_why()
    {
        var p = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100_000, Person, Now).Value!;

        var result = p.MarkHeld(90_000, Now);

        Assert.Equal("payment.amount_mismatch", result.Error!.Code);
        Assert.Equal(PaymentStatus.Failed, p.Status);
        Assert.Contains("90000", p.Audit.Last().Reason);
    }

    [Fact]
    public void The_shops_confirmation_releases_now_but_the_suppliers_mark_only_starts_72_hours()
    {
        var byShop = Held();
        var bySupplier = Held();

        Assert.True(byShop.Delivered(100_000, byShop: true, Now));
        Assert.False(bySupplier.Delivered(100_000, byShop: false, Now));

        Assert.Equal(Now + TimeSpan.FromHours(72), bySupplier.ReleaseDueAt);
        Assert.False(bySupplier.IsDueForRelease(Now.AddHours(71)));
        Assert.True(bySupplier.IsDueForRelease(Now.AddHours(72)));
    }

    [Fact]
    public void The_shop_confirming_after_the_supplier_marked_it_still_releases_straight_away()
    {
        var p = Held();
        p.Delivered(100_000, byShop: false, Now);

        Assert.True(p.Delivered(100_000, byShop: true, Now.AddHours(5)));
    }

    [Fact]
    public void A_dispute_freezes_the_payment_so_the_72_hours_cannot_release_it()
    {
        var p = Held();
        p.Delivered(100_000, byShop: false, Now);
        p.Freeze(Person, Now.AddHours(1));

        Assert.False(p.IsDueForRelease(Now.AddDays(10)));
        Assert.False(p.Delivered(100_000, byShop: true, Now.AddDays(10)));

        p.Unfreeze(Person, Now.AddDays(11));
        Assert.True(p.IsDueForRelease(Now.AddDays(11)));
    }

    [Fact]
    public void A_short_delivery_pays_the_supplier_less_and_gives_the_rest_back_to_the_shop()
    {
        var p = Held(100_000);
        p.Delivered(70_000, byShop: true, Now);

        var settled = p.Settle(p.AmountToRelease, "shop confirmed", null, Now);

        Assert.True(settled.IsSuccess);
        Assert.Equal((PaymentStatus.Released, 70_000L, 30_000L), (p.Status, p.ReleasedCents, p.RefundedCents));
        Assert.Equal(100_000, p.ReleasedCents + p.RefundedCents);
    }

    [Fact]
    public void A_full_refund_is_a_refunded_payment_and_money_can_only_move_once()
    {
        var p = Held();

        Assert.True(p.Settle(0, "dispute", Person, Now).IsSuccess);

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal("payment.not_held", p.Settle(100_000, "again", null, Now).Error!.Code);
        Assert.Equal("payment.release_invalid", Held().Settle(100_001, "too much", null, Now).Error!.Code);
    }

    [Fact]
    public void Every_state_change_is_audited_with_who_and_why()
    {
        var p = Held();
        p.Freeze(Person, Now);
        p.Unfreeze(Person, Now);
        p.Settle(100_000, "ok", Person, Now);

        Assert.Equal(
            [(null, PaymentStatus.Pending), (PaymentStatus.Pending, PaymentStatus.Held), (PaymentStatus.Held, PaymentStatus.Held), (PaymentStatus.Held, PaymentStatus.Held), (PaymentStatus.Held, PaymentStatus.Released)],
            p.Audit.OrderBy(a => a.CreatedAt).Select(a => (a.FromStatus, a.ToStatus)));
    }
}

public class DisputeDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static Dispute Raised() => Dispute.Raise(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DisputeSide.Shop, "Two cases were missing", Now).Value!;

    [Fact]
    public void A_dispute_needs_a_reason_of_reasonable_length()
    {
        Assert.Equal("dispute.reason_required", Dispute.Raise(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DisputeSide.Shop, "  no ", Now).Error!.Code);
        Assert.Equal("dispute.reason_too_long", Dispute.Raise(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DisputeSide.Shop, new string('x', 1001), Now).Error!.Code);
    }

    [Fact]
    public void An_admin_resolves_it_once_with_a_note()
    {
        var d = Raised();

        Assert.Equal("dispute.note_required", d.Resolve(DisputeOutcome.RefundShop, " ", Guid.NewGuid(), Now).Error!.Code);
        Assert.True(d.Resolve(DisputeOutcome.RefundShop, "Photos show the damage", Guid.NewGuid(), Now).IsSuccess);
        Assert.Equal((DisputeStatus.Resolved, DisputeOutcome.RefundShop), (d.Status, d.Outcome!.Value));
        Assert.Equal("dispute.already_resolved", d.Resolve(DisputeOutcome.PaySupplier, "changed my mind", Guid.NewGuid(), Now).Error!.Code);
    }
}
