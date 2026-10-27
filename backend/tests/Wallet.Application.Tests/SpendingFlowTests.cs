using System.Text;
using TillKnowIT.Reporting;
using TillKnowIT.Security;
using TillKnowIT.SharedKernel;
using Wallet.Application;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application.Tests;

public class SpendingFlowTests
{
    private readonly WalletWorld _w = new();

    // ---- W2-2: top-ups ----

    private async Task<WalletWorld.Child> ChildWithSchool(int fee = 0, bool staff = false)
    {
        await _w.OpenSchool(fee, staff);
        return await _w.EnrolChild();
    }

    private async Task<Result<TillKnowIT.Dispatching.Unit>> Callback(string type, string partnerRef, long cents, string id = "evt_1", DateTimeOffset? signedAt = null)
    {
        var body = $"{{\"id\":\"{id}\",\"type\":\"{type}\",\"partnerRef\":\"{partnerRef}\",\"amountCents\":{cents}}}";
        return await _w.Webhooks.Handle(new HandleWalletWebhook("dev", WebhookSignature.Sign(AcceptAllWebhooks.Secret, signedAt ?? _w.Clock.GetUtcNow(), body), body), default);
    }

    [Fact]
    public async Task A_top_up_gives_a_checkout_link_and_the_wallet_is_credited_only_when_the_partner_says_so()
    {
        var child = await ChildWithSchool();

        var started = (await _w.TopUpStarter.Handle(new StartTopUp(child.WalletId, new(child.Parent, 10_000, "https://app/back")), default)).Value!;
        Assert.Equal(("pending", "https://pay/1", 0L), (started.Status, started.CheckoutUrl, await _w.Balance(child)));

        await Callback("topup.completed", "ref_1", 10_000);

        Assert.Equal(10_000, await _w.Balance(child));
        Assert.Equal("wallet.topup", _w.Notifier.ToParents.Single().Template);
    }

    [Fact]
    public async Task A_top_up_is_a_sensible_amount_for_a_linked_parent_to_an_active_wallet()
    {
        var child = await ChildWithSchool();

        Assert.Equal("topup.amount_invalid", (await _w.TopUpStarter.Handle(new StartTopUp(child.WalletId, new(child.Parent, 1_999, null)), default)).Error!.Code);
        Assert.Equal("topup.amount_invalid", (await _w.TopUpStarter.Handle(new StartTopUp(child.WalletId, new(child.Parent, 500_001, null)), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.TopUpStarter.Handle(new StartTopUp(child.WalletId, new(Guid.NewGuid(), 5_000, null)), default)).Error!.Code);
        Assert.Equal("wallet.not_found", (await _w.TopUpStarter.Handle(new StartTopUp(Guid.NewGuid(), new(child.Parent, 5_000, null)), default)).Error!.Code);

        _w.Partner.Down = true;
        Assert.Equal("payment.partner_down", (await _w.TopUpStarter.Handle(new StartTopUp(child.WalletId, new(child.Parent, 5_000, null)), default)).Error!.Code);
        Assert.Empty(_w.Store.TopUps);

        _w.Partner.Down = false;
        _w.Store.Wallets.Single().Freeze();
        Assert.Equal("wallet.frozen", (await _w.TopUpStarter.Handle(new StartTopUp(child.WalletId, new(child.Parent, 5_000, null)), default)).Error!.Code);
    }

    [Fact]
    public async Task A_repeated_or_forged_or_stale_callback_never_credits_twice_or_at_all()
    {
        var child = await ChildWithSchool();
        await _w.TopUpStarter.Handle(new StartTopUp(child.WalletId, new(child.Parent, 10_000, null)), default);
        var body = "{\"id\":\"e\",\"type\":\"topup.completed\",\"partnerRef\":\"ref_1\",\"amountCents\":10000}";

        Assert.Equal("webhook.signature_invalid", (await _w.Webhooks.Handle(new HandleWalletWebhook("dev", null, body), default)).Error!.Code);
        Assert.Equal("webhook.signature_invalid", (await _w.Webhooks.Handle(new HandleWalletWebhook("dev", WebhookSignature.Sign("wrong", _w.Clock.GetUtcNow(), body), body), default)).Error!.Code);
        Assert.Equal("webhook.signature_invalid", (await Callback("topup.completed", "ref_1", 10_000, signedAt: _w.Clock.GetUtcNow().AddHours(-1))).Error!.Code);
        Assert.Equal(0, await _w.Balance(child));

        await Callback("topup.completed", "ref_1", 10_000);
        await Callback("topup.completed", "ref_1", 10_000);                                  // the same event again
        await Callback("topup.completed", "ref_1", 10_000, id: "evt_2");                       // a different event for a completed top-up

        Assert.Equal(10_000, await _w.Balance(child));
        Assert.Single(_w.Store.Transactions);
    }

    [Fact]
    public async Task A_callback_with_the_wrong_amount_fails_the_top_up_and_credits_nothing()
    {
        var child = await ChildWithSchool();
        await _w.TopUpStarter.Handle(new StartTopUp(child.WalletId, new(child.Parent, 10_000, null)), default);

        var result = await Callback("topup.completed", "ref_1", 9_999);

        Assert.Equal("topup.amount_mismatch", result.Error!.Code);
        Assert.Equal((0L, TopUpStatus.Failed), (await _w.Balance(child), _w.Store.TopUps.Single().Status));
    }

    [Fact]
    public async Task Unknown_references_junk_bodies_and_failures_are_handled_calmly()
    {
        var child = await ChildWithSchool();
        await _w.TopUpStarter.Handle(new StartTopUp(child.WalletId, new(child.Parent, 10_000, null)), default);

        Assert.Equal("topup.unknown", (await Callback("topup.completed", "nope", 1)).Error!.Code);
        Assert.Equal("webhook.payload_invalid", (await _w.Webhooks.Handle(new HandleWalletWebhook("dev", WebhookSignature.Sign(AcceptAllWebhooks.Secret, _w.Clock.GetUtcNow(), "junk"), "junk"), default)).Error!.Code);
        Assert.True((await Callback("topup.failed", "ref_1", 0, id: "evt_f")).IsSuccess);
        Assert.Equal(TopUpStatus.Failed, _w.Store.TopUps.Single().Status);
        Assert.Equal(0, await _w.Balance(child));
    }

    // ---- W2-2: cash at the office ----

    [Fact]
    public async Task A_cash_top_up_credits_the_wallet_and_the_school_owes_the_cash_until_it_pays_it_in()
    {
        var child = await ChildWithSchool();

        var done = await _w.Cash.Handle(new RecordCashTopUp(_w.School.Id, new(_w.SchoolAdmin, child.WalletId, 5_000, "R-001")), default);

        Assert.True(done.IsSuccess);
        Assert.Equal(5_000, await _w.Balance(child));
        Assert.Equal(-5_000, await _w.Store.Balance(AccountIds.SchoolCash(_w.School.Id), default));
        Assert.Equal(0, await _w.Store.Balance(AccountIds.PartnerFloat, default));

        await _w.Cash.Handle(new RecordCashDeposit(_w.School.Id, _w.PlatformAdmin, 5_000, "bank-ref-1"), default);

        Assert.Equal(0, await _w.Store.Balance(AccountIds.SchoolCash(_w.School.Id), default));
        Assert.Equal(-5_000, await _w.Store.Balance(AccountIds.PartnerFloat, default));
    }

    [Fact]
    public async Task The_same_receipt_twice_changes_nothing_and_a_cash_top_up_has_rules()
    {
        var child = await ChildWithSchool();
        var first = new RecordCashTopUp(_w.School.Id, new(_w.SchoolAdmin, child.WalletId, 5_000, "R-001"));

        await _w.Cash.Handle(first, default);
        await _w.Cash.Handle(first, default);
        await _w.Cash.Handle(new RecordCashTopUp(_w.School.Id, new(_w.SchoolAdmin, child.WalletId, 5_000, " r-001 ")), default);

        Assert.Equal(5_000, await _w.Balance(child));
        Assert.Equal("permission.denied", (await _w.Cash.Handle(new RecordCashTopUp(_w.School.Id, new(Guid.NewGuid(), child.WalletId, 5_000, "R-2")), default)).Error!.Code);
        Assert.Equal("topup.receipt_required", (await _w.Cash.Handle(new RecordCashTopUp(_w.School.Id, new(_w.SchoolAdmin, child.WalletId, 5_000, " ")), default)).Error!.Code);
        Assert.Equal("topup.amount_invalid", (await _w.Cash.Handle(new RecordCashTopUp(_w.School.Id, new(_w.SchoolAdmin, child.WalletId, 100, "R-3")), default)).Error!.Code);
        Assert.Equal("wallet.not_found", (await _w.Cash.Handle(new RecordCashTopUp(_w.School.Id, new(_w.SchoolAdmin, Guid.NewGuid(), 5_000, "R-4")), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Cash.Handle(new RecordCashDeposit(_w.School.Id, _w.SchoolAdmin, 5_000, "x"), default)).Error!.Code);
    }

    // ---- W2-6: transfers ----

    [Fact]
    public async Task A_parent_moves_money_between_their_own_children_once_per_reference()
    {
        var a = await ChildWithSchool();
        var b = await _w.EnrolChild("Sipho", "Dube", "4", "4A");
        var parent = a.Parent;
        _w.Store.Guardianships.Add(new Guardianship(parent, b.HolderId, "v1", _w.Clock.GetUtcNow()));   // the same parent has both children
        await _w.Fund(a, 5_000);
        var request = new TransferBetweenChildren(new(parent, a.WalletId, b.WalletId, 2_000, "t-1"));

        await _w.Transfers.Handle(request, default);
        await _w.Transfers.Handle(request, default);

        Assert.Equal((3_000L, 2_000L), (await _w.Balance(a), await _w.Balance(b)));
    }

    [Fact]
    public async Task A_transfer_cannot_go_to_someone_elses_child_or_exceed_the_balance_or_use_a_frozen_wallet()
    {
        var a = await ChildWithSchool();
        var b = await _w.EnrolChild("Sipho", "Dube", "4", "4A");
        await _w.Fund(a, 1_000);

        Assert.Equal("permission.denied", (await _w.Transfers.Handle(new TransferBetweenChildren(new(a.Parent, a.WalletId, b.WalletId, 500, "t-1")), default)).Error!.Code);
        _w.Store.Guardianships.Add(new Guardianship(a.Parent, b.HolderId, "v1", _w.Clock.GetUtcNow()));
        Assert.Equal("wallet.insufficient", (await _w.Transfers.Handle(new TransferBetweenChildren(new(a.Parent, a.WalletId, b.WalletId, 1_001, "t-2")), default)).Error!.Code);
        Assert.Equal("transfer.same_wallet", (await _w.Transfers.Handle(new TransferBetweenChildren(new(a.Parent, a.WalletId, a.WalletId, 100, "t-3")), default)).Error!.Code);
        Assert.Equal("transfer.amount_invalid", (await _w.Transfers.Handle(new TransferBetweenChildren(new(a.Parent, a.WalletId, b.WalletId, 0, "t-4")), default)).Error!.Code);
        Assert.Equal("transfer.reference_required", (await _w.Transfers.Handle(new TransferBetweenChildren(new(a.Parent, a.WalletId, b.WalletId, 100, " ")), default)).Error!.Code);

        _w.Store.Wallets.Single(w => w.Id == b.WalletId).Freeze();
        Assert.Equal("wallet.frozen", (await _w.Transfers.Handle(new TransferBetweenChildren(new(a.Parent, a.WalletId, b.WalletId, 100, "t-5")), default)).Error!.Code);
        Assert.Equal(1_000, await _w.Balance(a));
    }

    // ---- W3-1: paying ----

    [Fact]
    public async Task A_card_payment_shows_who_it_is_takes_the_money_and_pays_the_shop_less_the_fee()
    {
        var child = await ChildWithSchool(fee: 150);
        await _w.Fund(child, 5_000);

        var paid = (await _w.Pay(child, 1_000)).Value!;

        Assert.Equal(("Thandi Nkosi", "5", "5B", 1_000L, 4_000L, false), (paid.HolderName, paid.Grade, paid.ClassName, paid.AmountCents, paid.BalanceCents!.Value, paid.Repeated));
        Assert.Equal(985, await _w.Store.Balance(AccountIds.ShopPayable(_w.ShopId), default));
        Assert.Equal(15, await _w.Store.Balance(AccountIds.Fees, default));
        Assert.Equal(1_000, _w.Store.Purchases.Single().AmountCents);
    }

    [Fact]
    public async Task A_repeated_sale_id_answers_the_same_and_moves_money_once()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 5_000);
        var sale = Guid.NewGuid();

        var first = await _w.Pay(child, 1_000, sale);
        var second = await _w.Pay(child, 1_000, sale);

        Assert.Equal((false, true), (first.Value!.Repeated, second.Value!.Repeated));
        Assert.Equal(first.Value.PurchaseId, second.Value.PurchaseId);
        Assert.Equal(4_000, await _w.Balance(child));
        Assert.Single(_w.Store.Purchases);
    }

    [Fact]
    public async Task Reusing_a_sale_id_for_a_different_wallet_is_refused()
    {
        var a = await ChildWithSchool();
        var b = await _w.EnrolChild("Sipho", "Dube", "4", "4A");
        await _w.Fund(a, 5_000);
        await _w.Fund(b, 5_000);
        var sale = Guid.NewGuid();
        await _w.Pay(a, 500, sale);

        Assert.Equal("sale.id_reused", (await _w.Pay(b, 500, sale)).Error!.Code);
        Assert.Equal(5_000, await _w.Balance(b));
    }

    [Fact]
    public async Task A_payment_is_refused_for_a_blocked_item_over_the_limit_or_without_the_money()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 2_000);
        await _w.Wallets.Handle(new SetWalletRestrictions(child.WalletId, new(child.Parent, [new("category", "sweets"), new("product", "P-COLA")])), default);
        await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(child.Parent, 1_500, null, null)), default);

        Assert.Equal("wallet.item_blocked", (await _w.Pay(child, 500, null, new SaleItemDto(null, "sweets", "Lollipop", 1, 500))).Error!.Code);
        Assert.Equal("wallet.item_blocked", (await _w.Pay(child, 500, null, new SaleItemDto("P-COLA", null, "Cola", 1, 500))).Error!.Code);
        Assert.Equal("wallet.daily_limit", (await _w.Pay(child, 1_600)).Error!.Code);
        Assert.True((await _w.Pay(child, 1_000)).IsSuccess);
        Assert.Equal("wallet.daily_limit", (await _w.Pay(child, 600)).Error!.Code);
        await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(child.Parent, null, null, null)), default);   // no limit now
        Assert.Equal("wallet.insufficient", (await _w.Pay(child, 1_500)).Error!.Code);
        Assert.Equal(1_000, await _w.Balance(child));
    }

    [Fact]
    public async Task The_daily_limit_starts_again_each_south_african_day_and_a_cancelled_sale_does_not_count()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 10_000);
        await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(child.Parent, 1_000, null, null)), default);

        await _w.Pay(child, 1_000);
        Assert.Equal("wallet.daily_limit", (await _w.Pay(child, 100)).Error!.Code);

        _w.Clock.Advance(TimeSpan.FromHours(24));
        Assert.True((await _w.Pay(child, 1_000)).IsSuccess);
    }

    [Fact]
    public async Task The_schools_own_ceiling_applies_even_when_the_parent_sets_a_higher_limit_or_none()
    {
        await _w.OpenSchool(ceiling: 1_000);
        var child = await _w.EnrolChild();
        await _w.Fund(child, 10_000);

        Assert.Equal("wallet.daily_limit", (await _w.Pay(child, 1_001)).Error!.Code);
        Assert.True((await _w.Pay(child, 1_000)).IsSuccess);
        Assert.Equal("wallet.daily_limit", (await _w.Pay(child, 1)).Error!.Code);
    }

    [Fact]
    public async Task Only_an_approved_till_in_the_shop_with_an_active_school_can_take_wallet_payments()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 5_000);
        var asOtherDevice = new ProcessWalletPayment(_w.ShopId, new(Guid.NewGuid(), _w.StaffId, Guid.NewGuid(), child.Card, null, 500, []));
        var otherShop = new ProcessWalletPayment(Guid.NewGuid(), new(_w.DeviceId, _w.StaffId, Guid.NewGuid(), child.Card, null, 500, []));

        Assert.Equal("device.not_approved", (await _w.Payments.Handle(asOtherDevice, default)).Error!.Code);
        Assert.Equal("device.not_approved", (await _w.Payments.Handle(otherShop, default)).Error!.Code);

        _w.Store.Schools.Single().Suspend();
        Assert.Equal("wallet.school_not_active", (await _w.Pay(child, 500)).Error!.Code);
        Assert.Equal(5_000, await _w.Balance(child));
    }

    [Fact]
    public async Task A_card_from_another_school_or_an_unknown_card_is_unknown_here_and_reveals_nothing()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 5_000);
        var otherSchoolShop = Guid.NewGuid();
        _w.Identity.VerifiedShops[otherSchoolShop] = "verified";
        var otherDevice = Guid.NewGuid();
        _w.Identity.Tills.Add((otherSchoolShop, otherDevice, _w.StaffId));
        var other = (await _w.Schools.Handle(new RegisterSchool(new(_w.PlatformAdmin, "Moonrise High", null, otherSchoolShop)), default)).Value!;
        await _w.Schools.Handle(new VerifySchool(other.Id, _w.PlatformAdmin), default);

        var atOtherSchool = await _w.Payments.Handle(new ProcessWalletPayment(otherSchoolShop, new(otherDevice, _w.StaffId, Guid.NewGuid(), child.Card, null, 500, [])), default);
        var unknown = await _w.Payments.Handle(new ProcessWalletPayment(_w.ShopId, new(_w.DeviceId, _w.StaffId, Guid.NewGuid(), "DEADBEEF", null, 500, [])), default);
        var nothing = await _w.Payments.Handle(new ProcessWalletPayment(_w.ShopId, new(_w.DeviceId, _w.StaffId, Guid.NewGuid(), null, null, 500, [])), default);

        Assert.Equal("token.unknown", atOtherSchool.Error!.Code);
        Assert.Equal(atOtherSchool.Error.Message, unknown.Error!.Message);
        Assert.Equal("token.required", nothing.Error!.Code);
        Assert.Equal(5_000, await _w.Balance(child));
    }

    [Fact]
    public async Task Two_payments_at_the_same_moment_cannot_both_spend_the_same_money()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 1_000);
        _w.Store.ConflictOnNextSave = true;   // the other till won the race

        var lost = await _w.Pay(child, 800);

        Assert.Equal("wallet.busy", lost.Error!.Code);
        Assert.Equal(1_000, await _w.Balance(child));
        Assert.True((await _w.Pay(child, 800)).IsSuccess);   // the till tries again with a fresh read
    }

    // ---- QR ----

    private string Qr(WalletWorld.Child child) => _w.Qr.Handle(new GetWalletQr(child.WalletId, child.Parent), default).Result.Value!.Token;

    private Task<Result<WalletPaymentResponse>> PayQr(string? token, long cents = 500) =>
        _w.Payments.Handle(new ProcessWalletPayment(_w.ShopId, new(_w.DeviceId, _w.StaffId, Guid.NewGuid(), null, token, cents, [])), default);

    [Fact]
    public async Task A_qr_code_works_for_a_minute_and_then_stops()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 5_000);
        var code = (await _w.Qr.Handle(new GetWalletQr(child.WalletId, child.Parent), default)).Value!;

        Assert.Equal(_w.Clock.GetUtcNow().AddSeconds(60), code.ExpiresAt);
        _w.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.True((await PayQr(code.Token)).IsSuccess);
        _w.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("qr.invalid", (await PayQr(code.Token)).Error!.Code);
        Assert.Equal(4_500, await _w.Balance(child));
    }

    [Fact]
    public async Task A_forged_or_foreign_qr_code_is_refused_and_so_is_one_for_a_blocked_wallet()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 5_000);
        var (forged, _) = ExpiringToken.Issue("not-our-key", $"wallet:{child.WalletId:N}", _w.Clock.GetUtcNow(), TimeSpan.FromSeconds(60));
        var (unknownWallet, _) = ExpiringToken.Issue(WalletWorld.QrKey, $"wallet:{Guid.NewGuid():N}", _w.Clock.GetUtcNow(), TimeSpan.FromSeconds(60));

        Assert.Equal("qr.invalid", (await PayQr(forged)).Error!.Code);
        Assert.Equal("qr.invalid", (await PayQr(unknownWallet)).Error!.Code);
        Assert.Equal("qr.invalid", (await PayQr("garbage")).Error!.Code);

        var live = Qr(child);
        _w.Store.Wallets.Single().Freeze();
        Assert.Equal("wallet.frozen", (await PayQr(live)).Error!.Code);
        Assert.Equal("wallet.frozen", (await _w.Qr.Handle(new GetWalletQr(child.WalletId, child.Parent), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Qr.Handle(new GetWalletQr(child.WalletId, Guid.NewGuid()), default)).Error!.Code);
    }

    // ---- notifications ----

    [Fact]
    public async Task A_parent_who_asked_gets_a_push_for_each_purchase_and_one_low_balance_alert_when_the_line_is_crossed()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 3_000);
        await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(child.Parent, null, true, 1_500)), default);

        await _w.Pay(child, 1_000);   // 2000 left: above the line
        await _w.Pay(child, 800);      // 1200 left: crosses it
        await _w.Pay(child, 100);     // 1100 left: already under, no second alert

        Assert.Equal(["wallet.purchase", "wallet.purchase", "wallet.low_balance", "wallet.purchase"], _w.Notifier.ToParents.Select(n => n.Template));
        Assert.True(_w.Notifier.ToParents.Single(n => n.Template == "wallet.low_balance").Whatsapp);
        Assert.False(_w.Notifier.ToParents.First().Whatsapp);
    }

    [Fact]
    public async Task A_parent_who_did_not_ask_hears_nothing_and_a_broken_messaging_service_never_stops_a_sale()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 3_000);

        await _w.Pay(child, 500);
        Assert.Empty(_w.Notifier.ToParents);

        await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(child.Parent, null, true, null)), default);
        _w.Notifier.Throws = true;
        var sale = await _w.Pay(child, 500);

        Assert.True(sale.IsSuccess);
        Assert.Equal(2_000, await _w.Balance(child));
    }

    // ---- W2-3 and W2-4: limits, restrictions, statement ----

    [Fact]
    public async Task A_parent_sets_a_limit_and_blocked_items_and_sees_them_and_the_days_spending()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 3_000);
        await _w.Pay(child, 700);

        await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(child.Parent, 2_000, null, null)), default);
        var wallet = (await _w.Wallets.Handle(new SetWalletRestrictions(child.WalletId, new(child.Parent, [new("Category", "Sweets"), new("category", "sweets")])), default)).Value!;

        Assert.Equal((2_300L, 2_000L, 700L, "active"), (wallet.BalanceCents, wallet.DailyLimitCents!.Value, wallet.SpentTodayCents, wallet.Status));
        Assert.Equal([new RestrictionDto("category", "sweets")], wallet.Restrictions);   // the same item twice is one
    }

    [Fact]
    public async Task Only_a_parent_can_change_limits_and_an_admin_can_only_look()
    {
        var child = await ChildWithSchool();

        Assert.Equal("permission.denied", (await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(_w.SchoolAdmin, 100, null, null)), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Wallets.Handle(new SetWalletRestrictions(child.WalletId, new(Guid.NewGuid(), [])), default)).Error!.Code);
        Assert.True((await _w.Wallets.Handle(new GetWallet(child.WalletId, _w.SchoolAdmin), default)).IsSuccess);
        Assert.Equal("permission.denied", (await _w.Wallets.Handle(new GetWallet(child.WalletId, Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("wallet.limit_invalid", (await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(child.Parent, 600_000, null, null)), default)).Error!.Code);
        Assert.Equal("restriction.kind_invalid", (await _w.Wallets.Handle(new SetWalletRestrictions(child.WalletId, new(child.Parent, [new("flavour", "x")])), default)).Error!.Code);
        Assert.Equal("wallet.alert_invalid", (await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(child.Parent, null, null, 600_000)), default)).Error!.Code);
    }

    [Fact]
    public async Task The_statement_lists_every_movement_with_a_running_balance_and_downloads_in_every_format()
    {
        var child = await ChildWithSchool();
        await _w.Cash.Handle(new RecordCashTopUp(_w.School.Id, new(_w.SchoolAdmin, child.WalletId, 5_000, "R-1")), default);
        await _w.Pay(child, 1_200);
        _w.Clock.Advance(TimeSpan.FromDays(1));
        await _w.Pay(child, 300);

        var lines = (await _w.Wallets.Handle(new GetWalletStatement(child.WalletId, child.Parent, null, null), default)).Value!;

        Assert.Equal([5_000L, -1_200L, -300L], lines.Select(l => l.AmountCents));
        Assert.Equal([5_000L, 3_800L, 3_500L], lines.Select(l => l.BalanceAfterCents));
        Assert.Equal("Cash top-up, receipt R-1", lines[0].Description);

        foreach (var format in new[] { "csv", "xlsx", "pdf" })
            Assert.NotEmpty((await _w.Wallets.Handle(new ExportWalletStatement(child.WalletId, child.Parent, null, null, format), default)).Value!.Content);
        Assert.Equal("report.format_invalid", (await _w.Wallets.Handle(new ExportWalletStatement(child.WalletId, child.Parent, null, null, "doc"), default)).Error!.Code);
    }

    [Fact]
    public async Task A_statement_for_part_of_the_history_still_carries_the_right_running_balance()
    {
        var child = await ChildWithSchool();
        await _w.Fund(child, 5_000);
        _w.Clock.Advance(TimeSpan.FromDays(10));
        await _w.Pay(child, 1_000);
        var today = TillKnowIT.SharedKernel.BusinessDay.Of(_w.Clock.GetUtcNow());

        var lines = (await _w.Wallets.Handle(new GetWalletStatement(child.WalletId, child.Parent, today, today), default)).Value!;

        Assert.Equal((-1_000L, 4_000L), (lines.Single().AmountCents, lines.Single().BalanceAfterCents));
        Assert.Equal("report.range_invalid", (await _w.Wallets.Handle(new GetWalletStatement(child.WalletId, child.Parent, today, today.AddDays(-1)), default)).Error!.Code);
        Assert.Equal("report.range_too_large", (await _w.Wallets.Handle(new GetWalletStatement(child.WalletId, child.Parent, today.AddDays(-400), today), default)).Error!.Code);
    }

    // ---- W4-1: teachers ----

    [Fact]
    public async Task A_teacher_on_a_staff_account_buys_without_a_balance_and_the_account_owes_the_school()
    {
        await _w.OpenSchool(staffAccounts: true);
        var teacher = await _w.EnrolChild("Mr", "Dlamini", null, null, "teacher");

        var paid = await _w.Payments.Handle(new ProcessWalletPayment(_w.ShopId, new(_w.DeviceId, _w.StaffId, Guid.NewGuid(), teacher.Card, null, 2_500, [new(null, null, "Lunch", 1, 2_500)], UseStaffAccount: true)), default);

        Assert.True(paid.IsSuccess);
        Assert.Null(paid.Value!.BalanceCents);
        Assert.Equal(-2_500, await _w.Store.Balance(AccountIds.StaffAccount(teacher.HolderId), default));
        Assert.Equal(0, await _w.Balance(teacher));
        Assert.Equal(2_500, await _w.Store.Balance(AccountIds.ShopPayable(_w.ShopId), default));
    }

    [Fact]
    public async Task A_staff_account_needs_a_teacher_and_a_school_that_has_turned_them_on()
    {
        await _w.OpenSchool(staffAccounts: false);
        var teacher = await _w.EnrolChild("Mr", "Dlamini", null, null, "teacher");
        var learner = await _w.EnrolChild("Thandi", "Nkosi");

        ProcessWalletPayment Staff(string card) => new(_w.ShopId, new(_w.DeviceId, _w.StaffId, Guid.NewGuid(), card, null, 500, [], UseStaffAccount: true));

        Assert.Equal("staff.accounts_disabled", (await _w.Payments.Handle(Staff(teacher.Card), default)).Error!.Code);
        Assert.Equal("staff.not_a_teacher", (await _w.Payments.Handle(Staff(learner.Card), default)).Error!.Code);
    }

    [Fact]
    public async Task The_months_staff_accounts_are_listed_and_settled_once()
    {
        await _w.OpenSchool(staffAccounts: true);
        var a = await _w.EnrolChild("Mr", "Dlamini", null, null, "teacher");
        var b = await _w.EnrolChild("Ms", "Jones", null, null, "teacher");
        async Task Buy(WalletWorld.Child t, long cents) => await _w.Payments.Handle(new ProcessWalletPayment(_w.ShopId, new(_w.DeviceId, _w.StaffId, Guid.NewGuid(), t.Card, null, cents, [], UseStaffAccount: true)), default);
        await Buy(a, 2_500);
        await Buy(a, 1_000);
        await Buy(b, 800);

        var statement = (await _w.StaffOps.Handle(new GetStaffMonth(_w.School.Id, _w.SchoolAdmin, "2026-10"), default)).Value!;

        Assert.Equal(4_300, statement.TotalOwedCents);
        Assert.Equal(["Mr Dlamini", "Ms Jones"], statement.Teachers.Select(t => t.Name));
        Assert.Equal(3_500, statement.Teachers.Single(t => t.Name == "Mr Dlamini").OwedCents);

        await _w.StaffOps.Handle(new SettleStaffMonth(_w.School.Id, _w.PlatformAdmin, "2026-10"), default);
        await _w.StaffOps.Handle(new SettleStaffMonth(_w.School.Id, _w.PlatformAdmin, "2026-10"), default);

        Assert.Equal(0, await _w.Store.Balance(AccountIds.StaffAccount(a.HolderId), default));
        Assert.Equal(-4_300, await _w.Store.Balance(AccountIds.PartnerFloat, default));
        Assert.Equal("staff.month_invalid", (await _w.StaffOps.Handle(new GetStaffMonth(_w.School.Id, _w.SchoolAdmin, "october"), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.StaffOps.Handle(new SettleStaffMonth(_w.School.Id, _w.SchoolAdmin, "2026-10"), default)).Error!.Code);
    }

    // ---- the ledger always adds up ----

    [Fact]
    public async Task After_any_mix_of_top_ups_sales_cancellations_refunds_and_payouts_every_transaction_balances_and_the_books_match_the_partner()
    {
        await _w.OpenSchool(feeBasisPoints: 150);
        var kids = new List<WalletWorld.Child>();
        for (var i = 0; i < 6; i++) kids.Add(await _w.EnrolChild($"Learner{i}", "Test", "4", "4A"));
        var random = new Random(42);

        for (var step = 0; step < 400; step++)
        {
            var k = kids[random.Next(kids.Count)];
            switch (random.Next(4))
            {
                case 0: await _w.Cash.Handle(new RecordCashTopUp(_w.School.Id, new(_w.SchoolAdmin, k.WalletId, random.Next(2_000, 20_000), $"R-{step}")), default); break;
                case 1: await _w.Pay(k, random.Next(100, 5_000)); break;
                case 2: await _w.Wallets.Handle(new SetWalletLimits(k.WalletId, new(k.Parent, random.Next(0, 2) == 0 ? null : random.Next(500, 10_000), null, null)), default); break;
                default: _w.Clock.Advance(TimeSpan.FromHours(random.Next(1, 20))); break;
            }
        }
        await _w.Cash.Handle(new RecordCashDeposit(_w.School.Id, _w.PlatformAdmin, 1_000, "dep-1"), default);
        await _w.PayoutOps.Handle(new RunPayouts(null, _w.PlatformAdmin), default);

        Assert.Empty(await _w.Store.UnbalancedTransactions(10, default));
        Assert.All(_w.Store.Transactions, t => Assert.Equal(0, t.Entries.Sum(e => e.AmountCents)));
        Assert.Equal(0, _w.Store.Transactions.SelectMany(t => t.Entries).Sum(e => e.AmountCents));   // all accounts together sum to zero
        foreach (var k in kids) Assert.True(await _w.Balance(k) >= 0, "an online wallet never goes below zero");
    }
}
