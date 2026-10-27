using TillKnowIT.SharedKernel;
using Wallet.Domain;

namespace Wallet.Application.Tests;

public class LedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Wallet1 = Guid.NewGuid();
    private static readonly Guid Wallet2 = Guid.NewGuid();
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Holder = Guid.NewGuid();

    [Fact]
    public void A_transaction_must_add_up_to_exactly_zero_have_two_entries_and_no_zero_entry_and_a_reference()
    {
        string Code(string reference, params LedgerLeg[] legs) => LedgerTransaction.Create(LedgerTransactionType.Transfer, reference, legs, Now).Error!.Code;

        Assert.Equal("ledger.unbalanced", Code("r", new LedgerLeg(AccountIds.Wallet(Wallet1), -100), new LedgerLeg(AccountIds.Wallet(Wallet2), 99)));
        Assert.Equal("ledger.legs_required", Code("r", new LedgerLeg(AccountIds.Wallet(Wallet1), 0)));
        Assert.Equal("ledger.zero_entry", Code("r", new LedgerLeg(AccountIds.Wallet(Wallet1), -100), new LedgerLeg(AccountIds.Wallet(Wallet2), 100), new LedgerLeg(AccountIds.Fees, 0)));
        Assert.Equal("ledger.reference_invalid", Code("", new LedgerLeg(AccountIds.Wallet(Wallet1), -100), new LedgerLeg(AccountIds.Wallet(Wallet2), 100)));
        Assert.Equal("ledger.reference_invalid", Code(new string('x', 101), new LedgerLeg(AccountIds.Wallet(Wallet1), -100), new LedgerLeg(AccountIds.Wallet(Wallet2), 100)));
    }

    [Fact]
    public void Every_kind_of_posting_balances_to_zero_for_any_amount_and_fee()
    {
        var random = new Random(1234);
        for (var i = 0; i < 2_000; i++)
        {
            var cents = random.NextInt64(1, 200_001);
            var bps = random.Next(0, School.MaxFeeBasisPoints + 1);
            var fee = Postings.Fee(cents, bps);

            foreach (var legs in new[]
                     {
                         Postings.TopUp(Wallet1, cents), Postings.Purchase(Wallet1, Shop, cents, fee), Postings.StaffPurchase(Holder, Shop, cents, fee),
                         Postings.Reversal(Wallet1, Shop, cents, fee), Postings.Transfer(Wallet1, Wallet2, cents), Postings.Refund(Wallet1, cents),
                         Postings.Payout(Shop, cents), Postings.StaffSettlement(Holder, cents),
                     })
            {
                var made = LedgerTransaction.Create(LedgerTransactionType.Purchase, $"r{i}", legs, Now);
                Assert.True(made.IsSuccess, $"{cents} cents at {bps} bps: {made.Error?.Message}");
                Assert.Equal(0, made.Value!.Entries.Sum(e => e.AmountCents));
            }
        }
    }

    [Fact]
    public void A_purchase_pays_the_shop_the_sale_less_the_fee_and_the_fee_goes_to_the_fees_account()
    {
        var legs = Postings.Purchase(Wallet1, Shop, 10_000, 150);

        Assert.Equal(-10_000, legs.Single(l => l.AccountId == AccountIds.Wallet(Wallet1)).AmountCents);
        Assert.Equal(9_850, legs.Single(l => l.AccountId == AccountIds.ShopPayable(Shop)).AmountCents);
        Assert.Equal(150, legs.Single(l => l.AccountId == AccountIds.Fees).AmountCents);
        Assert.DoesNotContain(Postings.Purchase(Wallet1, Shop, 10_000, 0), l => l.AccountId == AccountIds.Fees);
    }

    [Fact]
    public void Fees_round_half_up_and_are_never_more_than_the_sale()
    {
        Assert.Equal(0, Postings.Fee(10_000, 0));
        Assert.Equal(150, Postings.Fee(10_000, 150));
        Assert.Equal(2, Postings.Fee(150, 150));      // 2.25 rounds to 2
        Assert.Equal(4, Postings.Fee(250, 150));      // 3.75 rounds to 4
        Assert.Equal(0, Postings.Fee(2, 1_000));      // a tiny sale carries no fee, so the shop's leg is never zero
    }

    [Fact]
    public void Account_ids_are_the_same_every_time_and_different_for_every_owner()
    {
        Assert.Equal(AccountIds.Wallet(Wallet1), AccountIds.Wallet(Wallet1));
        Assert.NotEqual(AccountIds.Wallet(Wallet1), AccountIds.Wallet(Wallet2));
        Assert.NotEqual(AccountIds.Wallet(Wallet1), AccountIds.ShopPayable(Wallet1));
        Assert.NotEqual(AccountIds.PartnerFloat, AccountIds.Fees);
    }
}

public class SpendRuleTests
{
    private static SpendContext Context(long balance = 10_000, long? limit = null, long? ceiling = null, long spent = 0, params WalletRestriction[] restrictions) =>
        new(HolderStatus.Active, WalletStatus.Active, limit, ceiling, spent, balance, restrictions);

    private static readonly SaleItem Crisps = new("p-crisps", "snacks", "Crisps", 1, 1_000);

    private static string? Code(SpendContext c, long amount = 1_000, params SaleItem[] items) => Rules(c, amount, items).Error?.Code;
    private static Result Rules(SpendContext c, long amount, SaleItem[] items) => SpendRules.Check(c, amount, items.Length == 0 ? [Crisps] : items);

    [Fact]
    public void A_purchase_within_balance_and_limit_is_allowed()
    {
        Assert.Null(Code(Context(balance: 1_000)));
    }

    [Fact]
    public void Reasons_come_in_a_fixed_order_inactive_frozen_amount_blocked_limit_balance()
    {
        Assert.Equal("wallet.holder_inactive", Code(Context() with { HolderStatus = HolderStatus.Left }));
        Assert.Equal("wallet.frozen", Code(Context() with { WalletStatus = WalletStatus.Frozen }));
        Assert.Equal("wallet.amount_invalid", Code(Context(), amount: 0));
        Assert.Equal("wallet.amount_invalid", Code(Context(), amount: SpendRules.MaxPurchaseCents + 1));
        Assert.Equal("wallet.item_blocked", Code(Context(restrictions: new WalletRestriction(Guid.NewGuid(), RestrictionKind.Category, "snacks", DateTimeOffset.UtcNow))));
        Assert.Equal("wallet.daily_limit", Code(Context(limit: 500)));
        Assert.Equal("wallet.insufficient", Code(Context(balance: 999)));
    }

    [Fact]
    public void Blocked_items_match_by_product_or_category_without_regard_to_case_and_name_the_item()
    {
        var byProduct = new WalletRestriction(Guid.NewGuid(), RestrictionKind.Product, "P-CRISPS", DateTimeOffset.UtcNow);
        var byCategory = new WalletRestriction(Guid.NewGuid(), RestrictionKind.Category, "Snacks", DateTimeOffset.UtcNow);
        var other = new WalletRestriction(Guid.NewGuid(), RestrictionKind.Category, "sweets", DateTimeOffset.UtcNow);

        Assert.Equal("Crisps is blocked on this wallet.", Rules(Context(restrictions: byProduct), 1_000, [Crisps]).Error!.Message);
        Assert.Equal("wallet.item_blocked", Code(Context(restrictions: byCategory)));
        Assert.Null(Code(Context(restrictions: other)));
    }

    [Fact]
    public void The_parents_limit_and_the_schools_ceiling_both_apply_and_the_lower_wins()
    {
        Assert.Equal(300, SpendRules.EffectiveLimit(500, 300));
        Assert.Equal(300, SpendRules.EffectiveLimit(300, 500));
        Assert.Equal(500, SpendRules.EffectiveLimit(500, null));
        Assert.Equal(500, SpendRules.EffectiveLimit(null, 500));
        Assert.Null(SpendRules.EffectiveLimit(null, null));

        Assert.Equal("wallet.daily_limit", Code(Context(limit: 5_000, ceiling: 1_500, spent: 600), amount: 1_000));
        Assert.Null(Code(Context(limit: 5_000, ceiling: 1_500, spent: 500), amount: 1_000));
    }

    [Fact]
    public void An_allowance_is_the_balance_capped_by_what_is_left_of_the_days_limit()
    {
        Assert.Equal(2_000, SpendRules.Allowance(2_000, null, 0));
        Assert.Equal(1_500, SpendRules.Allowance(2_000, 2_000, 500));
        Assert.Equal(300, SpendRules.Allowance(300, 2_000, 0));
        Assert.Equal(0, SpendRules.Allowance(2_000, 500, 700));
        Assert.Equal(0, SpendRules.Allowance(-50, null, 0));
    }

    [Fact]
    public void Offline_spending_is_not_checked_against_a_balance_only_against_the_other_rules()
    {
        Assert.Null(SpendRules.Check(Context(balance: 0), 1_000, [Crisps], checkBalance: false).Error?.Code);
        Assert.Equal("wallet.frozen", SpendRules.Check(Context(balance: 0) with { WalletStatus = WalletStatus.Frozen }, 1_000, [Crisps], checkBalance: false).Error!.Code);
    }
}

public class AllowanceTests
{
    private const string Key = "dev-allowance-key-0123456789";
    private static readonly Guid WalletId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 26);

    [Fact]
    public void A_signed_allowance_reads_back_and_a_changed_or_foreign_one_does_not()
    {
        var token = Allowances.Issue(Key, new(WalletId, Day, 5_000, 0, 2));

        Assert.Equal(new SignedAllowance(WalletId, Day, 5_000, 0, 2), Allowances.Verify(Key, token));
        Assert.Null(Allowances.Verify("another-key", token));
        Assert.Null(Allowances.Verify(Key, token + "x"));
        Assert.Null(Allowances.Verify(Key, null));
        Assert.Null(Allowances.Verify(Key, SignedToken(Key, "allowance|nonsense")));
        Assert.Null(Allowances.Verify(Key, SignedToken(Key, $"allowance|{WalletId:N}|2026-10-26|5000|2|2")));   // share outside the shares
        Assert.Null(Allowances.Verify(Key, SignedToken(Key, $"allowance|{WalletId:N}|2026-10-26|-5|0|1")));
        Assert.Null(Allowances.Verify(Key, SignedToken(Key, $"other|{WalletId:N}|2026-10-26|5000|0|1")));
    }

    private static string SignedToken(string key, string payload) => TillKnowIT.Security.SignedToken.Sign(key, payload);

    [Fact]
    public void Each_till_gets_an_equal_share_rounded_down_so_together_they_never_exceed_the_total()
    {
        var allowance = new SignedAllowance(WalletId, Day, 1_000, 0, 3);

        Assert.Equal(333, allowance.PerTillCents);
        Assert.True(allowance.PerTillCents * 3 <= 1_000);
        Assert.Equal(1_000, new SignedAllowance(WalletId, Day, 1_000, 0, 1).PerTillCents);
    }
}

public class HolderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static Holder Learner(string first = "Thandi", string last = "Nkosi") => Holder.Create(Guid.NewGuid(), new(first, last, "5", "5B", HolderType.Learner), Now).Value!;

    [Fact]
    public void A_holder_needs_both_names_and_trims_them()
    {
        Assert.Equal("holder.name_required", Holder.Create(Guid.NewGuid(), new(" ", "Nkosi", null, null, HolderType.Learner), Now).Error!.Code);
        Assert.Equal("holder.name_too_long", Holder.Create(Guid.NewGuid(), new(new string('a', 81), "Nkosi", null, null, HolderType.Learner), Now).Error!.Code);
        Assert.Equal("Thandi Nkosi", Learner(" Thandi ", " Nkosi ").DisplayName);
    }

    [Fact]
    public void Duplicates_are_spotted_regardless_of_case_and_spacing()
    {
        Assert.Equal(Holder.DuplicateKey("thandi", "NKOSI", "5", "5b"), Learner().Key);
        Assert.Equal(Holder.DuplicateKey("Thandi", "Nkosi  ", " 5", "5B"), Learner().Key);
        Assert.NotEqual(Holder.DuplicateKey("Thandi", "Nkosi", "6", "5B"), Learner().Key);
    }

    [Fact]
    public void A_link_code_is_eight_easy_characters_shown_once_and_only_its_hash_is_kept()
    {
        var holder = Learner();

        var code = holder.IssueLinkCode();

        Assert.Equal(8, code.Length);
        Assert.DoesNotContain(code, c => "ILOU01".Contains(c));
        Assert.True(holder.LinkCodeMatches(code));
        Assert.True(holder.LinkCodeMatches(code.ToLowerInvariant().Insert(4, "-")));
        Assert.False(holder.LinkCodeMatches("AAAAAAAA"));
        Assert.False(holder.LinkCodeMatches(null));
        Assert.DoesNotContain(code, holder.LinkCodeHash!);
        Assert.NotEqual(code, holder.IssueLinkCode());   // a new code replaces the old one
        Assert.False(holder.LinkCodeMatches(code));
    }

    [Fact]
    public void Erasure_removes_everything_personal_but_keeps_the_row_the_ledger_points_to()
    {
        var holder = Learner();
        holder.SetPhoto("tkfile://photos/1");
        holder.IssueLinkCode();

        holder.Erase();

        Assert.Equal(("Removed", "Removed", null, null, null, HolderStatus.Erased), (holder.FirstName, holder.LastName, holder.Grade, holder.PhotoUrl, holder.LinkCodeHash, holder.Status));
        Assert.Equal("Removed", holder.DisplayName);
        Assert.Equal("holder.erased", holder.Update("A", "B", null, null).Error!.Code);
    }

    [Fact]
    public void A_token_hash_ignores_case_colons_and_spaces_and_a_blocked_token_stays_blocked()
    {
        Assert.Equal(HolderToken.Hash("04:A2:2B:1C"), HolderToken.Hash("04a22b1c"));
        Assert.NotEqual(HolderToken.Hash("04A22B1C"), HolderToken.Hash("04A22B1D"));

        var token = new HolderToken(Guid.NewGuid(), TokenType.Nfc, "04A22B1C", Now);
        token.Block(Now);
        token.Block(Now.AddDays(1));
        Assert.Equal((TokenStatus.Blocked, Now), (token.Status, token.BlockedAt!.Value));
        Assert.DoesNotContain("04A22B1C", token.TokenHash);
    }
}

public class SchoolTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_school_needs_a_name_and_an_EMIS_number_of_digits_and_starts_pending()
    {
        Assert.Equal("school.name_invalid", School.Register(" ", null, Guid.NewGuid(), Now).Error!.Code);
        Assert.Equal("school.emis_invalid", School.Register("Sunrise Primary", "ABC123", Guid.NewGuid(), Now).Error!.Code);

        var school = School.Register("Sunrise Primary", "200301234", Guid.NewGuid(), Now).Value!;
        Assert.Equal((SchoolStatus.Pending, false), (school.Status, school.IsTrading));
        school.Verify(Guid.NewGuid(), Now);
        Assert.True(school.IsTrading);
    }

    [Fact]
    public void Settings_are_bounded()
    {
        var school = School.Register("Sunrise Primary", null, Guid.NewGuid(), Now).Value!;

        Assert.Equal("school.fee_invalid", school.Configure(1_001, PayoutSchedule.Daily, false, null, 60).Error!.Code);
        Assert.Equal("school.ceiling_invalid", school.Configure(100, PayoutSchedule.Daily, false, 600_000, 60).Error!.Code);
        Assert.Equal("school.split_invalid", school.Configure(100, PayoutSchedule.Daily, false, null, 1).Error!.Code);
        Assert.True(school.Configure(150, PayoutSchedule.Monthly, true, 20_000, 30).IsSuccess);
        Assert.Equal((150, PayoutSchedule.Monthly, true, 20_000L, 30), (school.FeeBasisPoints, school.PayoutSchedule, school.StaffAccountsEnabled, school.DailyCeilingCents!.Value, school.OfflineSplitMinutes));
    }
}

public class PreOrderTests
{
    // Monday 26 October 2026, 08:00 in South Africa
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 8, 0, 0, BusinessDay.Offset);
    private static readonly BreakSlot Lunch = new(Guid.NewGuid(), "lunch", "Lunch", new TimeOnly(9, 30), Now);

    private static Result<PreOrder> Place(DateOnly collectOn, DateTimeOffset? now = null) =>
        PreOrder.Place(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Lunch, collectOn, "[]", 2_500, Guid.NewGuid(), now ?? Now);

    [Fact]
    public void An_order_can_be_placed_before_the_cut_off_on_a_school_day_up_to_a_week_ahead()
    {
        Assert.True(Place(new DateOnly(2026, 10, 26)).IsSuccess);        // today, 08:00, cut-off 09:30
        Assert.True(Place(new DateOnly(2026, 11, 2)).IsSuccess);         // a week ahead (Monday)
        Assert.Equal("preorder.date_invalid", Place(new DateOnly(2026, 11, 3)).Error!.Code);
        Assert.Equal("preorder.date_invalid", Place(new DateOnly(2026, 10, 25)).Error!.Code);
        Assert.Equal("preorder.not_a_school_day", Place(new DateOnly(2026, 10, 31)).Error!.Code);   // Saturday
    }

    [Fact]
    public void The_cut_off_is_in_south_african_time_and_passing_it_stops_both_ordering_and_cancelling()
    {
        Assert.Equal("preorder.cut_off_passed", Place(new DateOnly(2026, 10, 26), new DateTimeOffset(2026, 10, 26, 9, 30, 0, BusinessDay.Offset)).Error!.Code);

        var order = Place(new DateOnly(2026, 10, 26)).Value!;
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 30, 0, BusinessDay.Offset), order.CutOffAt);
        Assert.Equal("preorder.cut_off_passed", order.Cancel(Now.AddHours(2)).Error!.Code);
        Assert.True(order.Cancel(Now.AddHours(1)).IsSuccess);
        Assert.Equal("preorder.not_cancellable", order.Cancel(Now).Error!.Code);
    }

    [Fact]
    public void A_placed_order_is_made_ready_and_collected_once()
    {
        var order = Place(new DateOnly(2026, 10, 26)).Value!;

        Assert.True(order.MarkReady().IsSuccess);
        Assert.Equal("preorder.not_placed", order.MarkReady().Error!.Code);
        Assert.True(order.Collect().IsSuccess);
        Assert.Equal("preorder.not_collectable", order.Collect().Error!.Code);
        Assert.Equal("preorder.not_cancellable", order.Cancel(Now).Error!.Code);
    }
}

public class MoneyRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_refund_is_for_what_is_in_the_wallet_when_the_school_confirms_never_more()
    {
        Assert.Equal("refund.nothing_to_refund", RefundRequest.Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Now).Error!.Code);
        var request = RefundRequest.Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5_000, Now).Value!;

        Assert.True(request.Approve(Guid.NewGuid(), 3_200).IsSuccess);

        Assert.Equal((3_200L, RefundStatus.Approved), (request.AmountCents, request.Status));
        Assert.Equal("refund.not_requested", request.Approve(Guid.NewGuid(), 3_200).Error!.Code);
        Assert.Equal("refund.not_requested", request.Reject(Guid.NewGuid()).Error!.Code);
    }

    [Fact]
    public void A_refund_that_finds_the_wallet_empty_is_not_approved()
    {
        var request = RefundRequest.Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5_000, Now).Value!;
        Assert.Equal("refund.nothing_to_refund", request.Approve(Guid.NewGuid(), 0).Error!.Code);
        Assert.Equal(RefundStatus.Requested, request.Status);
    }

    [Fact]
    public void A_difference_with_the_partner_is_a_mismatch_that_needs_a_written_resolution()
    {
        var matched = WalletReconciliation.Run(new DateOnly(2026, 10, 26), 1_000_000, 1_000_000, Now);
        var off = WalletReconciliation.Run(new DateOnly(2026, 10, 26), 1_000_050, 1_000_000, Now);

        Assert.Equal((ReconciliationStatus.Matched, 0L), (matched.Status, matched.DifferenceCents));
        Assert.Equal((ReconciliationStatus.Mismatch, 50L), (off.Status, off.DifferenceCents));
        Assert.Equal("reconciliation.note_required", off.Resolve(Guid.NewGuid(), "ok").Error!.Code);
        Assert.True(off.Resolve(Guid.NewGuid(), "Partner fee of R0.50 booked late").IsSuccess);
        Assert.Equal("reconciliation.not_open", off.Resolve(Guid.NewGuid(), "again and again").Error!.Code);
        Assert.Equal("reconciliation.not_open", matched.Resolve(Guid.NewGuid(), "nothing to do here").Error!.Code);
    }

    [Fact]
    public void An_erasure_request_is_due_within_30_days()
    {
        var request = new ErasureRequest(Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.Equal(Now.AddDays(30), request.DueBy);
        request.Complete(Now.AddDays(2));
        request.Complete(Now.AddDays(9));
        Assert.Equal(Now.AddDays(2), request.CompletedAt);
    }
}
