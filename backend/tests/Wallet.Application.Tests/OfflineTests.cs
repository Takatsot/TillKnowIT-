using TillKnowIT.SharedKernel;
using Wallet.Application;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application.Tests;

public class OfflineTests
{
    private readonly WalletWorld _w = new();

    private async Task<WalletWorld.Child> Child(long balance = 5_000, string first = "Thandi", string last = "Nkosi")
    {
        if (_w.Store.Schools.Count == 0) await _w.OpenSchool();
        var child = await _w.EnrolChild(first, last, "5", "5B");
        if (balance > 0) await _w.Fund(child, balance);
        return child;
    }

    private async Task<TillSnapshot> Snapshot(Guid? device = null) =>
        (await _w.Snapshots.Handle(new GetTillSnapshot(_w.ShopId, device ?? _w.DeviceId, _w.StaffId), default)).Value!;

    private OfflineWalletSale Sale(WalletWorld.Child child, long cents, string allowanceToken, Guid? id = null, DateTimeOffset? soldAt = null) =>
        new(id ?? Guid.NewGuid(), child.WalletId, allowanceToken, cents, [new(null, null, "Crisps", 1, cents)], soldAt ?? _w.Clock.GetUtcNow());

    private Task<Result<SettleOfflineSalesResult>> Settle(Guid? device = null, params OfflineWalletSale[] sales) =>
        _w.Offline.Handle(new SettleOfflineSales(_w.ShopId, new(device ?? _w.DeviceId, _w.StaffId, [.. sales])), default);

    // ---- what the till is given ----

    [Fact]
    public async Task The_snapshot_gives_each_learner_a_signed_allowance_but_never_a_real_balance()
    {
        var thandi = await Child(5_000);
        var sipho = await Child(300, "Sipho", "Dube");
        await _w.Wallets.Handle(new SetWalletLimits(thandi.WalletId, new(thandi.Parent, 2_000, null, null)), default);

        var snapshot = await Snapshot();

        Assert.Equal(new DateOnly(2026, 10, 26), snapshot.Day);
        Assert.Equal(2_000, snapshot.Holders.Single(h => h.HolderId == thandi.HolderId).AllowanceCents);   // the limit is lower than the balance
        Assert.Equal(300, snapshot.Holders.Single(h => h.HolderId == sipho.HolderId).AllowanceCents);     // the balance is lower than no limit
        Assert.All(snapshot.Holders, h => Assert.Equal(h.AllowanceCents, Allowances.Verify(WalletWorld.AllowanceKey, h.AllowanceToken)!.TotalCents));
        Assert.DoesNotContain("5000", System.Text.Json.JsonSerializer.Serialize(snapshot.Holders));
    }

    [Fact]
    public async Task What_was_spent_today_comes_off_the_allowance_and_a_frozen_or_departed_wallet_gets_none()
    {
        var child = await Child(5_000);
        await _w.Wallets.Handle(new SetWalletLimits(child.WalletId, new(child.Parent, 2_000, null, null)), default);
        await _w.Pay(child, 800);
        var frozen = await Child(1_000, "Frozen", "Kid");
        _w.Store.Wallets.Single(w => w.Id == frozen.WalletId).Freeze();
        var left = await Child(1_000, "Gone", "Kid");
        await _w.HolderOps.Handle(new MarkHolderLeft(left.HolderId, _w.SchoolAdmin), default);

        var snapshot = await Snapshot();

        Assert.Equal(1_200, snapshot.Holders.Single(h => h.HolderId == child.HolderId).AllowanceCents);
        Assert.Equal(0, snapshot.Holders.Single(h => h.HolderId == frozen.HolderId).AllowanceCents);
        Assert.DoesNotContain(snapshot.Holders, h => h.HolderId == left.HolderId);
    }

    [Fact]
    public async Task The_snapshot_carries_card_blocks_and_blocked_items_and_only_for_an_approved_till()
    {
        var child = await Child();
        await _w.Wallets.Handle(new SetWalletRestrictions(child.WalletId, new(child.Parent, [new("category", "sweets")])), default);
        await _w.Wallets.Handle(new BlockMyChildsCards(child.HolderId, child.Parent), default);

        var snapshot = await Snapshot();

        Assert.Equal("blocked", snapshot.Tokens.Single().Status);
        Assert.Equal([new TillRestriction(child.WalletId, "category", "sweets")], snapshot.Restrictions);
        Assert.Equal("device.not_approved", (await _w.Snapshots.Handle(new GetTillSnapshot(_w.ShopId, Guid.NewGuid(), _w.StaffId), default)).Error!.Code);
        Assert.Equal("device.not_approved", (await _w.Snapshots.Handle(new GetTillSnapshot(Guid.NewGuid(), _w.DeviceId, _w.StaffId), default)).Error!.Code);
    }

    [Fact]
    public async Task A_photo_is_a_path_only_that_schools_tills_can_open_never_the_picture_itself()
    {
        var child = await Child();
        await _w.Photos.Handle(new SetHolderPhoto(child.HolderId, _w.SchoolAdmin, "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1]), default);

        var holder = (await Snapshot()).Holders.Single();

        Assert.Equal($"/api/v1/holders/{child.HolderId}/photo", holder.PhotoUrl);
    }

    // ---- settling ----

    [Fact]
    public async Task An_offline_sale_inside_the_allowance_is_settled_into_the_ledger_and_the_shop_is_paid()
    {
        var child = await Child(5_000);
        var token = (await Snapshot()).Holders.Single().AllowanceToken;

        var result = (await Settle(null, Sale(child, 1_500, token))).Value!;

        Assert.Equal("settled", result.Outcomes.Single().Outcome);
        Assert.Equal(3_500, await _w.Balance(child));
        Assert.Equal(1_500, await _w.Store.Balance(AccountIds.ShopPayable(_w.ShopId), default));
        Assert.True(_w.Store.Purchases.Single().Offline);
    }

    [Fact]
    public async Task Sending_the_same_sales_again_is_answered_the_same_and_moves_no_money()
    {
        var child = await Child(5_000);
        var token = (await Snapshot()).Holders.Single().AllowanceToken;
        var sale = Sale(child, 1_500, token);

        await Settle(null, sale);
        var again = (await Settle(null, sale)).Value!;

        Assert.Equal("settled", again.Outcomes.Single().Outcome);
        Assert.Equal(3_500, await _w.Balance(child));
        Assert.Single(_w.Store.Purchases);
    }

    [Fact]
    public async Task Sales_that_together_exceed_the_allowance_are_rejected_from_the_one_that_crosses_it()
    {
        var child = await Child(2_000);
        var token = (await Snapshot()).Holders.Single().AllowanceToken;

        var result = (await Settle(null, Sale(child, 1_200, token), Sale(child, 700, token), Sale(child, 200, token), Sale(child, 100, token))).Value!;

        Assert.Equal(["settled", "settled", "rejected", "settled"], result.Outcomes.Select(o => o.Outcome));   // 1200 + 700 = 1900; 200 would make 2100; 100 still fits
        Assert.Equal("allowance.exceeded", result.Outcomes[2].Reason);
        Assert.Equal(0, await _w.Balance(child));
        Assert.Equal(3, _w.Store.Purchases.Count);
    }

    [Fact]
    public async Task Two_tills_selling_offline_can_together_never_exceed_the_one_allowance()
    {
        var child = await Child(1_000);
        var token = (await Snapshot()).Holders.Single().AllowanceToken;

        var first = (await Settle(_w.DeviceId, Sale(child, 700, token))).Value!;
        var second = (await Settle(_w.DeviceId2, Sale(child, 700, token))).Value!;

        Assert.Equal(("settled", "rejected"), (first.Outcomes.Single().Outcome, second.Outcomes.Single().Outcome));
        Assert.Equal(300, await _w.Balance(child));
    }

    [Fact]
    public async Task A_sale_is_rejected_for_a_forged_wrong_wallet_wrong_day_or_other_schools_wallet_and_the_shop_owner_and_parent_are_told()
    {
        var child = await Child(5_000);
        var other = await Child(5_000, "Sipho", "Dube");
        var good = (await Snapshot()).Holders.Single(h => h.HolderId == child.HolderId).AllowanceToken;
        var yesterday = _w.AllowanceToken(child.WalletId, 5_000, new DateOnly(2026, 10, 25));
        var forged = Allowances.Issue("a-key-the-server-never-used", new SignedAllowance(child.WalletId, new DateOnly(2026, 10, 26), 5_000, 0, 1));

        var result = (await Settle(null,
            Sale(child, 500, forged), Sale(other, 500, good), Sale(child, 500, yesterday), Sale(child, 500, "garbage"))).Value!;

        Assert.Equal(["allowance.invalid", "allowance.wrong_wallet", "allowance.wrong_day", "allowance.invalid"], result.Outcomes.Select(o => o.Reason));
        Assert.All(result.Outcomes, o => Assert.Equal("rejected", o.Outcome));
        Assert.Equal(5_000, await _w.Balance(child));
        Assert.Equal(4, _w.Notifier.ToShops.Count(n => n.Template == "wallet.sale_rejected"));
        Assert.Contains(_w.Notifier.ToParents, n => n.Template == "wallet.sale_rejected" && n.Parent == child.Parent);
    }

    [Fact]
    public async Task A_sale_with_a_silly_amount_or_a_stale_or_future_time_is_rejected()
    {
        var child = await Child(5_000);
        var token = (await Snapshot()).Holders.Single().AllowanceToken;
        var now = _w.Clock.GetUtcNow();

        var result = (await Settle(null,
            Sale(child, 0, token), Sale(child, SpendRules.MaxPurchaseCents + 1, token),
            Sale(child, 500, token, soldAt: now.AddDays(-8)), Sale(child, 500, token, soldAt: now.AddHours(1)))).Value!;

        Assert.Equal(["sale.amount_invalid", "sale.amount_invalid", "sale.date_invalid", "sale.date_invalid"], result.Outcomes.Select(o => o.Reason));
        Assert.Equal(5_000, await _w.Balance(child));
    }

    [Fact]
    public async Task A_rejected_sale_stays_rejected_when_sent_again_even_if_it_would_now_fit()
    {
        var child = await Child(5_000);
        var sale = Sale(child, 500, "garbage");

        await Settle(null, sale);
        var again = (await Settle(null, sale)).Value!;

        Assert.Equal(("rejected", "allowance.invalid"), (again.Outcomes.Single().Outcome, again.Outcomes.Single().Reason));
        Assert.Single(_w.Notifier.ToShops);   // told once, not on every resend
    }

    [Fact]
    public async Task When_the_parent_spends_the_money_elsewhere_first_the_sale_still_settles_the_shop_is_paid_and_the_wallet_is_overdrawn()
    {
        var child = await Child(1_000);
        var token = (await Snapshot()).Holders.Single().AllowanceToken;   // the till was given 1000 this morning
        await _w.Transfers.Handle(new TransferBetweenChildren(new(child.Parent, child.WalletId, (await _w.EnrolChild("Sibling", "Nkosi", "3", "3A")).WalletId, 1_000, "t")), default);   // fails: not that parent's child
        await _w.Pay(child, 900);                                             // an online purchase empties most of it

        var result = (await Settle(null, Sale(child, 800, token))).Value!;

        Assert.Equal(("settled", true), (result.Outcomes.Single().Outcome, result.Outcomes.Single().Overdrawn));
        Assert.Equal(-700, await _w.Balance(child));
        Assert.Equal(1_700, await _w.Store.Balance(AccountIds.ShopPayable(_w.ShopId), default));
        Assert.Contains(_w.Notifier.ToParents, n => n.Template == "wallet.overdrawn");
    }

    [Fact]
    public async Task Only_an_approved_till_can_settle_and_too_big_a_batch_is_refused()
    {
        var child = await Child();
        var token = (await Snapshot()).Holders.Single().AllowanceToken;

        Assert.Equal("device.not_approved", (await _w.Offline.Handle(new SettleOfflineSales(_w.ShopId, new(Guid.NewGuid(), _w.StaffId, [Sale(child, 100, token)])), default)).Error!.Code);
        Assert.Equal("sync.too_large", (await Settle(null, [.. Enumerable.Range(0, 501).Select(_ => Sale(child, 1, token))])).Error!.Code);
        Assert.Empty((await Settle(null)).Value!.Outcomes);
    }

    [Fact]
    public async Task A_conflict_while_settling_stops_the_batch_and_sending_it_again_finishes_the_job()
    {
        var child = await Child(5_000);
        var token = (await Snapshot()).Holders.Single().AllowanceToken;
        var first = Sale(child, 500, token);
        var second = Sale(child, 700, token);
        _w.Store.ConflictOnNextSave = true;

        var busy = await Settle(null, first, second);
        var retry = (await Settle(null, first, second)).Value!;

        Assert.Equal("wallet.busy", busy.Error!.Code);
        Assert.All(retry.Outcomes, o => Assert.Equal("settled", o.Outcome));
        Assert.Equal(3_800, await _w.Balance(child));
        Assert.Equal(2, _w.Store.Purchases.Count);
    }

    // ---- the outage simulation ----

    [Fact]
    public async Task A_whole_school_day_offline_nobody_spends_above_their_allowance_and_every_sale_settles_correctly()
    {
        await _w.OpenSchool(feeBasisPoints: 150);
        var random = new Random(7);
        var kids = new List<WalletWorld.Child>();
        for (var i = 0; i < 40; i++)
        {
            var kid = await _w.EnrolChild($"Learner{i}", "Test", "4", "4A");
            await _w.Fund(kid, random.Next(0, 6) * 1_000);
            if (i % 5 == 0) await _w.Wallets.Handle(new SetWalletLimits(kid.WalletId, new(kid.Parent, random.Next(5, 15) * 100, null, null)), default);
            kids.Add(kid);
        }

        // Morning: the tills sync, then the connection goes. Each till splits every allowance between the two of them.
        var snapshot = await Snapshot();
        var allowance = snapshot.Holders.ToDictionary(h => h.WalletId);
        var perTill = allowance.ToDictionary(a => a.Key, a => a.Value.AllowanceCents / 2);
        var spentOffline = kids.ToDictionary(k => k.WalletId, _ => 0L);
        var sales = new List<(Guid Device, OfflineWalletSale Sale)>();

        for (var n = 0; n < 600; n++)
        {
            var kid = kids[random.Next(kids.Count)];
            var device = random.Next(2) == 0 ? _w.DeviceId : _w.DeviceId2;
            var cents = random.Next(1, 8) * 100;
            // A till sells only while it has allowance left; this is the rule the real till enforces.
            var used = sales.Where(s => s.Device == device && s.Sale.WalletId == kid.WalletId).Sum(s => s.Sale.AmountCents);
            if (used + cents > perTill[kid.WalletId]) continue;
            sales.Add((device, Sale(kid, cents, allowance[kid.WalletId].AllowanceToken)));
            spentOffline[kid.WalletId] += cents;
        }

        // Evening: the connection is back and both tills upload.
        foreach (var group in sales.GroupBy(s => s.Device))
            Assert.True((await Settle(group.Key, [.. group.Select(s => s.Sale)])).IsSuccess);

        foreach (var kid in kids)
        {
            var settled = _w.Store.Purchases.Where(p => p.WalletId == kid.WalletId).Sum(p => p.AmountCents);
            Assert.True(settled <= allowance[kid.WalletId].AllowanceCents, "nobody spends above their signed allowance");
            Assert.Equal(spentOffline[kid.WalletId], settled);
            Assert.True(await _w.Balance(kid) >= 0, "nobody is overdrawn when nothing was spent elsewhere");
        }
        Assert.All(_w.Store.OfflineRecords, r => Assert.Equal(OfflineSaleStatus.Settled, r.Status));
        Assert.Equal(sales.Sum(s => s.Sale.AmountCents), _w.Store.Purchases.Sum(p => p.AmountCents));
        Assert.Empty(await _w.Store.UnbalancedTransactions(5, default));
        Assert.Equal(0, _w.Store.Transactions.SelectMany(t => t.Entries).Sum(e => e.AmountCents));
    }
}
