using TillKnowIT.SharedKernel;
using Wallet.Application;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application.Tests;

public class PreOrderTests2
{
    private readonly WalletWorld _w = new();
    private WalletWorld.Child _child = default!;
    private MenuItemDto _toastie = default!;
    private MenuItemDto _juice = default!;

    private async Task Setup(long balance = 10_000, int fee = 0)
    {
        await _w.OpenSchool(feeBasisPoints: fee);
        _child = await _w.EnrolChild();
        await _w.Fund(_child, balance);
        await _w.Menu.Handle(new SaveBreaks(_w.School.Id, new(_w.SchoolAdmin, [new("lunch", "Lunch", "09:30"), new("break1", "First break", "07:30")])), default);
        _toastie = (await _w.Menu.Handle(new AddMenuItem(_w.School.Id, new(_w.SchoolAdmin, "Cheese toastie", 2_500, "P-TOASTIE", "meals", true)), default)).Value!;
        _juice = (await _w.Menu.Handle(new AddMenuItem(_w.School.Id, new(_w.SchoolAdmin, "Juice", 1_000, null, "drinks", true)), default)).Value!;
    }

    private static readonly DateOnly Today = new(2026, 10, 26);   // Monday; the world starts at 09:00

    private Task<Result<PreOrderResponse>> Place(DateOnly? day = null, string @break = "lunch", params PlacePreOrderLine[] lines) =>
        _w.PreOrders.Handle(new PlacePreOrder(new(_child.Parent, _child.HolderId, @break, day ?? Today, lines.Length == 0 ? [new(_toastie.Id, 1), new(_juice.Id, 2)] : [.. lines])), default);

    // ---- the menu ----

    [Fact]
    public async Task The_school_sets_breaks_and_a_menu_and_a_parent_sees_only_what_is_available()
    {
        await Setup();
        await _w.Menu.Handle(new ChangeMenuItem(_juice.Id, new(_w.SchoolAdmin, "Juice", 1_000, null, "drinks", false)), default);

        var forParent = (await _w.Menu.Handle(new GetMenuForChild(_child.HolderId, _child.Parent), default)).Value!;
        var forAdmin = (await _w.Menu.Handle(new GetSchoolMenu(_w.School.Id, _w.SchoolAdmin), default)).Value!;

        Assert.Equal(["Cheese toastie"], forParent.Items.Select(i => i.Name));
        Assert.Equal(2, forAdmin.Items.Count);
        Assert.Equal(["break1", "lunch"], forParent.Breaks.Select(b => b.Code));   // earliest cut-off first
        Assert.Equal("permission.denied", (await _w.Menu.Handle(new GetMenuForChild(_child.HolderId, Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Menu.Handle(new GetSchoolMenu(_w.School.Id, Guid.NewGuid()), default)).Error!.Code);
    }

    [Fact]
    public async Task Breaks_and_menu_items_are_validated_and_only_for_school_admins()
    {
        await Setup();

        Assert.Equal("break.code_invalid", (await _w.Menu.Handle(new SaveBreaks(_w.School.Id, new(_w.SchoolAdmin, [new("Bad Code!", "x", "09:00")])), default)).Error!.Code);
        Assert.Equal("break.cutoff_invalid", (await _w.Menu.Handle(new SaveBreaks(_w.School.Id, new(_w.SchoolAdmin, [new("a", "x", "9.30pm")])), default)).Error!.Code);
        Assert.Equal("break.duplicate", (await _w.Menu.Handle(new SaveBreaks(_w.School.Id, new(_w.SchoolAdmin, [new("a", "x", "09:00"), new("a", "y", "10:00")])), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Menu.Handle(new SaveBreaks(_w.School.Id, new(Guid.NewGuid(), [])), default)).Error!.Code);
        Assert.Equal("menu.price_invalid", (await _w.Menu.Handle(new AddMenuItem(_w.School.Id, new(_w.SchoolAdmin, "Free lunch", 0, null, null, true)), default)).Error!.Code);
        Assert.Equal("menu.name_invalid", (await _w.Menu.Handle(new AddMenuItem(_w.School.Id, new(_w.SchoolAdmin, " ", 100, null, null, true)), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Menu.Handle(new ChangeMenuItem(_juice.Id, new(Guid.NewGuid(), "Juice", 100, null, null, true)), default)).Error!.Code);
        Assert.Equal(2, _w.Store.BreakRows.Count);   // the bad attempts changed nothing
    }

    // ---- placing ----

    [Fact]
    public async Task A_pre_order_is_paid_from_the_wallet_at_the_menu_price_when_it_is_placed()
    {
        await Setup(fee: 150);

        var order = (await Place()).Value!;

        Assert.Equal((4_500L, "placed", Today), (order.TotalCents, order.Status, order.CollectOn));
        Assert.Equal(["Cheese toastie", "Juice"], order.Lines.Select(l => l.Name));
        Assert.Equal(5_500, await _w.Balance(_child));
        Assert.Equal(4_432, await _w.Store.Balance(AccountIds.ShopPayable(_w.ShopId), default));   // R45.00 less 1.5% (R0.675 rounds half up to R0.68)
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 30, 0, BusinessDay.Offset), order.CutOffAt);
    }

    [Fact]
    public async Task A_parent_cannot_set_the_price_and_a_pre_order_respects_blocked_items_and_the_balance()
    {
        await Setup(balance: 3_000);

        Assert.Equal("preorder.item_unavailable", (await Place(null, "lunch", new PlacePreOrderLine(Guid.NewGuid(), 1))).Error!.Code);
        Assert.Equal("wallet.insufficient", (await Place()).Error!.Code);                                   // R45 against R30
        await _w.Wallets.Handle(new SetWalletRestrictions(_child.WalletId, new(_child.Parent, [new("category", "drinks")])), default);
        Assert.Equal("wallet.item_blocked", (await Place(null, "lunch", new PlacePreOrderLine(_juice.Id, 1))).Error!.Code);
        Assert.Equal(3_000, await _w.Balance(_child));
        Assert.Empty(_w.Store.PreOrderRows);
    }

    [Fact]
    public async Task A_pre_order_needs_a_real_break_sensible_lines_a_linked_parent_and_a_date_before_the_cut_off()
    {
        await Setup();

        Assert.Equal("preorder.break_unknown", (await Place(null, "dinner")).Error!.Code);
        Assert.Equal("preorder.lines_invalid", (await _w.PreOrders.Handle(new PlacePreOrder(new(_child.Parent, _child.HolderId, "lunch", Today, [])), default)).Error!.Code);
        Assert.Equal("preorder.quantity_invalid", (await Place(null, "lunch", new PlacePreOrderLine(_toastie.Id, 21))).Error!.Code);
        Assert.Equal("preorder.item_repeated", (await Place(null, "lunch", new PlacePreOrderLine(_toastie.Id, 1), new PlacePreOrderLine(_toastie.Id, 1))).Error!.Code);
        Assert.Equal("permission.denied", (await _w.PreOrders.Handle(new PlacePreOrder(new(Guid.NewGuid(), _child.HolderId, "lunch", Today, [new(_toastie.Id, 1)])), default)).Error!.Code);
        Assert.Equal("preorder.not_a_school_day", (await Place(new DateOnly(2026, 10, 31))).Error!.Code);
        Assert.Equal("preorder.cut_off_passed", (await Place(Today, "break1")).Error!.Code);            // 07:30 has gone: it is 09:00
        Assert.True((await Place(Today.AddDays(1), "break1")).IsSuccess);                                // tomorrow is fine
    }

    // ---- cancelling ----

    [Fact]
    public async Task Cancelling_before_the_cut_off_gives_back_exactly_what_was_taken_fee_included()
    {
        await Setup(fee: 150);
        var order = (await Place()).Value!;

        var cancelled = await _w.PreOrders.Handle(new CancelPreOrder(order.Id, _child.Parent), default);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(10_000, await _w.Balance(_child));
        Assert.Equal(0, await _w.Store.Balance(AccountIds.ShopPayable(_w.ShopId), default));
        Assert.Equal(0, await _w.Store.Balance(AccountIds.Fees, default));
        Assert.Equal(PreOrderStatus.Cancelled, _w.Store.PreOrderRows.Single().Status);
        Assert.Equal(0, await WalletAccess.SpentToday(_w.Store, _child.WalletId, _w.Clock.GetUtcNow(), default));   // it no longer counts against the day
    }

    [Fact]
    public async Task Cancelling_after_the_cut_off_or_twice_or_by_a_stranger_is_refused_and_money_stays_put()
    {
        await Setup();
        var order = (await Place()).Value!;

        Assert.Equal("permission.denied", (await _w.PreOrders.Handle(new CancelPreOrder(order.Id, Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("preorder.not_found", (await _w.PreOrders.Handle(new CancelPreOrder(Guid.NewGuid(), _child.Parent), default)).Error!.Code);
        _w.Clock.Advance(TimeSpan.FromMinutes(40));   // 09:40, after the 09:30 cut-off
        Assert.Equal("preorder.cut_off_passed", (await _w.PreOrders.Handle(new CancelPreOrder(order.Id, _child.Parent), default)).Error!.Code);
        Assert.Equal(5_500, await _w.Balance(_child));
    }

    [Fact]
    public async Task A_cancelled_order_cannot_be_cancelled_again_to_be_refunded_twice()
    {
        await Setup();
        var order = (await Place(Today.AddDays(1))).Value!;

        await _w.PreOrders.Handle(new CancelPreOrder(order.Id, _child.Parent), default);
        var again = await _w.PreOrders.Handle(new CancelPreOrder(order.Id, _child.Parent), default);

        Assert.Equal("preorder.not_cancellable", again.Error!.Code);
        Assert.Equal(10_000, await _w.Balance(_child));
    }

    // ---- handing out ----

    [Fact]
    public async Task The_cashier_sees_todays_paid_orders_by_break_and_class_and_marks_them_collected_once()
    {
        await Setup();
        var other = await _w.EnrolChild("Sipho", "Dube", "4", "4A");
        await _w.Fund(other, 5_000);
        var mine = (await Place()).Value!;
        await _w.PreOrders.Handle(new PlacePreOrder(new(other.Parent, other.HolderId, "lunch", Today, [new(_juice.Id, 1)])), default);
        var cancelledLater = (await Place(Today.AddDays(1))).Value!;
        await _w.PreOrders.Handle(new CancelPreOrder(cancelledLater.Id, _child.Parent), default);

        var list = (await _w.TillPreOrders.Handle(new ListPreOrdersForTill(_w.ShopId, _w.DeviceId, _w.StaffId, null, null), default)).Value!;
        var collected = await _w.TillPreOrders.Handle(new CollectPreOrder(_w.ShopId, mine.Id, new(_w.DeviceId, _w.StaffId)), default);
        var twice = await _w.TillPreOrders.Handle(new CollectPreOrder(_w.ShopId, mine.Id, new(_w.DeviceId, _w.StaffId)), default);

        Assert.Equal(["Sipho Dube", "Thandi Nkosi"], list.Select(o => o.HolderName));   // by class: 4A before 5B
        Assert.True(collected.IsSuccess);
        Assert.Equal("preorder.not_collectable", twice.Error!.Code);
        Assert.Equal("device.not_approved", (await _w.TillPreOrders.Handle(new CollectPreOrder(_w.ShopId, mine.Id, new(Guid.NewGuid(), _w.StaffId)), default)).Error!.Code);
        Assert.Equal("preorder.not_cancellable", (await _w.PreOrders.Handle(new CancelPreOrder(mine.Id, _child.Parent), default)).Error!.Code);
    }

    [Fact]
    public async Task The_till_snapshot_includes_todays_paid_orders_so_pick_up_works_offline()
    {
        await Setup();
        await Place();

        var snapshot = (await _w.Snapshots.Handle(new GetTillSnapshot(_w.ShopId, _w.DeviceId, _w.StaffId), default)).Value!;

        var order = Assert.Single(snapshot.PreOrders);
        Assert.Equal(("Thandi Nkosi", "placed"), (order.HolderName, order.Status));
        Assert.Equal(2, order.Lines.Count);
    }

    [Fact]
    public async Task A_parent_lists_their_childs_orders_and_only_theirs()
    {
        await Setup();
        await Place();

        Assert.Single((await _w.PreOrders.Handle(new ListChildPreOrders(_child.HolderId, _child.Parent), default)).Value!);
        Assert.Equal("permission.denied", (await _w.PreOrders.Handle(new ListChildPreOrders(_child.HolderId, Guid.NewGuid()), default)).Error!.Code);
    }
}

public class MoneyFlowTests
{
    private readonly WalletWorld _w = new();

    private async Task<WalletWorld.Child> LeftChild(long balance)
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();
        await _w.Fund(child, balance);
        await _w.HolderOps.Handle(new MarkHolderLeft(child.HolderId, _w.SchoolAdmin), default);
        return child;
    }

    // ---- W5-2: refunds ----

    [Fact]
    public async Task A_parent_asks_for_the_money_back_the_school_confirms_and_the_partner_pays_it()
    {
        var child = await LeftChild(4_200);

        var asked = (await _w.RefundOps.Handle(new RequestRefund(child.WalletId, new(child.Parent)), default)).Value!;
        var done = (await _w.RefundOps.Handle(new ReviewRefund(asked.Id, new(_w.SchoolAdmin, true)), default)).Value!;

        Assert.Equal(("paid", 4_200L), (done.Status, done.AmountCents));
        Assert.Equal([(child.WalletId, 4_200L, $"refund:{asked.Id:N}")], _w.Partner.Refunds);
        Assert.Equal(0, await _w.Balance(child));
        Assert.Equal(WalletStatus.Closed, _w.Store.Wallets.Single().Status);
        Assert.Equal(0, await _w.Store.Balance(AccountIds.PartnerFloat, default));   // the money went in and has gone back out, so the partner holds none for us
    }

    [Fact]
    public async Task A_refund_is_for_a_child_who_has_left_with_money_in_the_wallet_asked_for_once()
    {
        await _w.OpenSchool();
        var still = await _w.EnrolChild();
        await _w.Fund(still, 1_000);
        Assert.Equal("refund.child_still_enrolled", (await _w.RefundOps.Handle(new RequestRefund(still.WalletId, new(still.Parent)), default)).Error!.Code);

        var empty = await _w.EnrolChild("Empty", "Wallet", "3", "3A");
        await _w.HolderOps.Handle(new MarkHolderLeft(empty.HolderId, _w.SchoolAdmin), default);
        Assert.Equal("refund.nothing_to_refund", (await _w.RefundOps.Handle(new RequestRefund(empty.WalletId, new(empty.Parent)), default)).Error!.Code);

        var left = await _w.EnrolChild("Left", "Child", "2", "2A");
        await _w.Fund(left, 500);
        await _w.HolderOps.Handle(new MarkHolderLeft(left.HolderId, _w.SchoolAdmin), default);
        Assert.True((await _w.RefundOps.Handle(new RequestRefund(left.WalletId, new(left.Parent)), default)).IsSuccess);
        Assert.Equal("refund.already_requested", (await _w.RefundOps.Handle(new RequestRefund(left.WalletId, new(left.Parent)), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.RefundOps.Handle(new RequestRefund(left.WalletId, new(Guid.NewGuid())), default)).Error!.Code);
    }

    [Fact]
    public async Task Only_the_school_decides_a_refund_and_a_partner_that_is_down_changes_nothing()
    {
        var child = await LeftChild(1_000);
        var asked = (await _w.RefundOps.Handle(new RequestRefund(child.WalletId, new(child.Parent)), default)).Value!;

        Assert.Equal("permission.denied", (await _w.RefundOps.Handle(new ReviewRefund(asked.Id, new(child.Parent, true)), default)).Error!.Code);
        _w.Partner.Down = true;
        Assert.Equal("payment.partner_down", (await _w.RefundOps.Handle(new ReviewRefund(asked.Id, new(_w.SchoolAdmin, true)), default)).Error!.Code);
        Assert.Equal(1_000, await _w.Balance(child));

        _w.Partner.Down = false;
        Assert.Equal("paid", (await _w.RefundOps.Handle(new ReviewRefund(asked.Id, new(_w.SchoolAdmin, true)), default)).Value!.Status);
        Assert.Equal("refund.not_requested", (await _w.RefundOps.Handle(new ReviewRefund(asked.Id, new(_w.SchoolAdmin, true)), default)).Error!.Code);
    }

    [Fact]
    public async Task A_rejected_refund_pays_nothing()
    {
        var child = await LeftChild(1_000);
        var asked = (await _w.RefundOps.Handle(new RequestRefund(child.WalletId, new(child.Parent)), default)).Value!;

        var rejected = (await _w.RefundOps.Handle(new ReviewRefund(asked.Id, new(_w.SchoolAdmin, false)), default)).Value!;

        Assert.Equal("rejected", rejected.Status);
        Assert.Empty(_w.Partner.Refunds);
        Assert.Equal(1_000, await _w.Balance(child));
    }

    // ---- W5-3: reconciliation ----

    [Fact]
    public async Task When_the_partner_and_the_ledger_agree_to_the_cent_the_day_is_matched()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();
        await _w.Cash.Handle(new RecordCashDeposit(_w.School.Id, _w.PlatformAdmin, 6_000, "dep"), default);   // the partner holds 6000 for the platform
        _w.Partner.Held = 6_000;

        var run = await _w.Reconcile();

        Assert.Equal((ReconciliationStatus.Matched, 0L, 6_000L), (run.Status, run.DifferenceCents, run.LedgerTotalCents));
        _ = child;
    }

    [Fact]
    public async Task A_difference_of_one_cent_is_a_mismatch_that_blocks_payouts_until_a_person_resolves_it()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();
        await _w.Fund(child, 5_000);
        await _w.Pay(child, 1_000);
        _w.Partner.Held = 5_001;   // the partner says one cent more than we do

        var run = await _w.Reconcile();
        var blocked = (await _w.PayoutOps.Handle(new RunPayouts(null, null), default)).Value!;

        Assert.Equal((ReconciliationStatus.Mismatch, 1L), (run.Status, run.DifferenceCents));
        Assert.True(blocked.Blocked);
        Assert.Contains("on hold", blocked.BlockedReason);
        Assert.Empty(_w.Partner.ShopPayouts);

        await _w.Reconciliations.Handle(new ResolveWalletReconciliation(run.Id, new(_w.PlatformAdmin, "One cent of partner rounding, written off")), default);
        var released = (await _w.PayoutOps.Handle(new RunPayouts(null, null), default)).Value!;

        Assert.False(released.Blocked);
        Assert.Single(_w.Partner.ShopPayouts);
    }

    [Fact]
    public async Task A_broken_ledger_transaction_is_flagged_even_when_the_totals_agree()
    {
        await _w.OpenSchool();
        _w.Partner.Held = 0;
        // Slip a transaction that does not add up past the domain, the way a bad migration or manual edit might.
        var bad = LedgerTransaction.Create(LedgerTransactionType.TopUp, "bad", [new(AccountIds.PartnerFloat, -100), new(AccountIds.Fees, 100)], _w.Clock.GetUtcNow()).Value!;
        typeof(LedgerEntry).GetProperty(nameof(LedgerEntry.AmountCents))!.SetValue(bad.Entries[1], 99L);
        await _w.Store.Add(bad, default);
        await _w.Store.Save(default);

        var run = await _w.Reconcile();

        Assert.Equal(ReconciliationStatus.Mismatch, run.Status);
        Assert.Contains("do not add up to zero", run.Note);
    }

    [Fact]
    public async Task Reconciliation_is_for_platform_admins_and_a_resolution_needs_a_real_note()
    {
        await _w.OpenSchool();
        _w.Partner.Held = 50;
        var run = await _w.Reconcile();

        Assert.Equal("permission.denied", (await _w.Reconciliations.Handle(new RunWalletReconciliation(new DateOnly(2026, 10, 26), _w.SchoolAdmin), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Reconciliations.Handle(new ResolveWalletReconciliation(run.Id, new(_w.SchoolAdmin, "all fine really")), default)).Error!.Code);
        Assert.Equal("reconciliation.note_required", (await _w.Reconciliations.Handle(new ResolveWalletReconciliation(run.Id, new(_w.PlatformAdmin, "ok")), default)).Error!.Code);
        Assert.Equal("reconciliation.not_found", (await _w.Reconciliations.Handle(new ResolveWalletReconciliation(Guid.NewGuid(), new(_w.PlatformAdmin, "something long enough")), default)).Error!.Code);
    }

    // ---- W5-1: payouts ----

    [Fact]
    public async Task A_due_payout_pays_the_shop_what_it_earned_once_a_day()
    {
        await _w.OpenSchool(feeBasisPoints: 150);
        var child = await _w.EnrolChild();
        await _w.Fund(child, 10_000);
        await _w.Pay(child, 4_000);

        var first = (await _w.PayoutOps.Handle(new RunPayouts(null, _w.PlatformAdmin), default)).Value!;
        var second = (await _w.PayoutOps.Handle(new RunPayouts(null, _w.PlatformAdmin), default)).Value!;

        Assert.Equal((3_940L, "paid"), (first.Payouts.Single().AmountCents, first.Payouts.Single().Status));
        Assert.Empty(second.Payouts);
        Assert.Equal([(_w.ShopId, 3_940L, $"payout:{_w.ShopId:N}:20261026")], _w.Partner.ShopPayouts);
        Assert.Equal(0, await _w.Store.Balance(AccountIds.ShopPayable(_w.ShopId), default));
    }

    [Fact]
    public async Task A_failed_payout_is_recorded_and_the_money_stays_owed_to_the_shop()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();
        await _w.Fund(child, 10_000);
        await _w.Pay(child, 4_000);
        _w.Partner.Down = true;

        var run = (await _w.PayoutOps.Handle(new RunPayouts(null, null), default)).Value!;

        Assert.Equal("failed", run.Payouts.Single().Status);
        Assert.Equal(4_000, await _w.Store.Balance(AccountIds.ShopPayable(_w.ShopId), default));

        _w.Partner.Down = false;
        Assert.Equal("paid", (await _w.PayoutOps.Handle(new RunPayouts(null, null), default)).Value!.Payouts.Single().Status);
    }

    [Fact]
    public async Task Payout_schedules_decide_the_day_and_only_the_platform_can_run_them_by_hand()
    {
        Assert.True(PayoutSchedules.IsDue(PayoutSchedule.Daily, new DateOnly(2026, 10, 27)));
        Assert.True(PayoutSchedules.IsDue(PayoutSchedule.Weekly, new DateOnly(2026, 10, 30)));    // a Friday
        Assert.False(PayoutSchedules.IsDue(PayoutSchedule.Weekly, new DateOnly(2026, 10, 29)));
        Assert.True(PayoutSchedules.IsDue(PayoutSchedule.Monthly, new DateOnly(2026, 10, 31)));
        Assert.False(PayoutSchedules.IsDue(PayoutSchedule.Monthly, new DateOnly(2026, 10, 30)));
        Assert.True(PayoutSchedules.IsDue(PayoutSchedule.Monthly, new DateOnly(2028, 2, 29)));

        await _w.OpenSchool();
        Assert.Equal("permission.denied", (await _w.PayoutOps.Handle(new RunPayouts(null, _w.SchoolAdmin), default)).Error!.Code);
    }

    [Fact]
    public async Task The_owner_sees_what_wallet_sales_earned_what_was_kept_and_what_was_paid_and_nobody_else_does()
    {
        await _w.OpenSchool(feeBasisPoints: 150);
        var child = await _w.EnrolChild();
        await _w.Fund(child, 10_000);
        await _w.Pay(child, 4_000);
        await _w.PayoutOps.Handle(new RunPayouts(null, null), default);
        await _w.Pay(child, 1_000);

        var summary = (await _w.PayoutOps.Handle(new GetShopWalletSales(_w.ShopId, _w.ShopOwner, null, null), default)).Value!;

        Assert.Equal((5_000L, 75L, 3_940L, 985L), (summary.SalesCents, summary.FeesCents, summary.PaidOutCents, summary.OwedToShopCents));
        Assert.Single(summary.Payouts);
        Assert.Equal("permission.denied", (await _w.PayoutOps.Handle(new GetShopWalletSales(_w.ShopId, Guid.NewGuid(), null, null), default)).Error!.Code);
    }

    // ---- W5-4: erasure ----

    [Fact]
    public async Task Erasing_a_childs_data_removes_everything_personal_but_keeps_the_ledger()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();
        await _w.Fund(child, 1_000);
        await _w.Pay(child, 1_000);
        await _w.Photos.Handle(new SetHolderPhoto(child.HolderId, _w.SchoolAdmin, "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1]), default);
        var transactions = _w.Store.Transactions.Count;

        var done = (await _w.ErasureOps.Handle(new RequestErasure(child.HolderId, child.Parent), default)).Value!;

        Assert.True(done.Completed);
        var holder = _w.Store.Holders.Single();
        Assert.Equal(("Removed", HolderStatus.Erased, null), (holder.FirstName, holder.Status, holder.PhotoUrl));
        Assert.Empty(_w.Files.Files);
        Assert.Empty(_w.Store.Guardianships);
        Assert.Equal(TokenStatus.Blocked, _w.Store.Tokens.Single().Status);
        Assert.Equal(transactions, _w.Store.Transactions.Count);   // the money history is kept
        Assert.Equal("token.blocked", (await _w.Pay(child, 100)).Error!.Code);
    }

    [Fact]
    public async Task A_wallet_with_money_in_it_is_not_erased_until_the_parent_has_been_refunded_and_then_it_completes()
    {
        var child = await LeftChild(2_500);

        var waiting = (await _w.ErasureOps.Handle(new RequestErasure(child.HolderId, child.Parent), default)).Value!;
        Assert.False(waiting.Completed);
        Assert.Equal(_w.Clock.GetUtcNow().AddDays(30), waiting.DueBy);
        Assert.Equal(HolderStatus.Left, _w.Store.Holders.Single().Status);

        var asked = (await _w.RefundOps.Handle(new RequestRefund(child.WalletId, new(child.Parent)), default)).Value!;
        await _w.RefundOps.Handle(new ReviewRefund(asked.Id, new(_w.SchoolAdmin, true)), default);
        var processed = await _w.ErasureOps.Handle(new ProcessPendingErasures(), default);

        Assert.Equal(1, processed.Value);
        Assert.Equal(HolderStatus.Erased, _w.Store.Holders.Single().Status);
        Assert.NotNull(_w.Store.Erasures.Single().CompletedAt);
    }

    [Fact]
    public async Task Only_a_linked_parent_can_ask_and_asking_twice_makes_one_request()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();

        Assert.Equal("permission.denied", (await _w.ErasureOps.Handle(new RequestErasure(child.HolderId, Guid.NewGuid()), default)).Error!.Code);
        var other = await _w.EnrolChild("Sipho", "Dube", "4", "4A");
        await _w.Fund(other, 100);
        await _w.ErasureOps.Handle(new RequestErasure(other.HolderId, other.Parent), default);
        await _w.ErasureOps.Handle(new RequestErasure(other.HolderId, other.Parent), default);

        Assert.Single(_w.Store.Erasures);
    }
}
