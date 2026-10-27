using System.Text.Json;
using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

// ---------------------------------------------------------------- W3-2: what a school's tills hold for the day

/// <summary>
/// What a till keeps so it can sell without a connection: each learner's name and photo marker, which cards are blocked, blocked items, today's
/// paid pre-orders, and a signed allowance per wallet. The till never holds a real balance, so it cannot spend money that is not there.
/// </summary>
public sealed record GetTillSnapshot(Guid ShopId, Guid DeviceId, Guid StaffId) : IQuery<TillSnapshot>;

internal sealed class TillSnapshotHandler(IWalletStore store, ITillAccessChecker tills, WalletKeys keys, TimeProvider clock) : IQueryHandler<GetTillSnapshot, TillSnapshot>
{
    public async Task<Result<TillSnapshot>> Handle(GetTillSnapshot q, CancellationToken ct)
    {
        var access = await tills.Check(q.ShopId, q.DeviceId, q.StaffId, "sale.create", ct);
        if (!access.IsSuccess) return Result<TillSnapshot>.Fail(access.Error!.Code, access.Error.Message);
        var school = await store.SchoolByShop(q.ShopId, ct);
        if (school is null || !school.IsTrading) return Result<TillSnapshot>.Fail("wallet.no_school", "This shop does not take wallet payments.");

        var now = clock.GetUtcNow();
        var day = BusinessDay.Of(now);
        var holders = (await store.HoldersOf(school.Id, ct)).Where(h => h.Status == HolderStatus.Active).ToList();
        var wallets = (await store.WalletsOf(school.Id, ct)).ToDictionary(w => w.HolderId);
        var balances = await store.Balances(wallets.Values.Select(w => w.AccountId).ToList(), ct);
        var spent = await store.SpentByWallet(school.Id, BusinessDay.Start(day), BusinessDay.Start(day.AddDays(1)), ct);

        var rows = new List<TillHolder>();
        foreach (var h in holders)
        {
            var w = wallets[h.Id];
            var limit = SpendRules.EffectiveLimit(w.DailyLimitCents, school.DailyCeilingCents);
            var allowance = w.Status == WalletStatus.Active ? SpendRules.Allowance(balances.GetValueOrDefault(w.AccountId), limit, spent.GetValueOrDefault(w.Id)) : 0;
            var token = Allowances.Issue(keys.AllowanceKey, new SignedAllowance(w.Id, day, allowance, 0, 1));
            rows.Add(new TillHolder(h.Id, w.Id, h.Type.ToString().ToLowerInvariant(), h.DisplayName, h.Grade, h.ClassName,
                h.PhotoUrl is null ? null : $"/api/v1/holders/{h.Id}/photo", w.Status.ToString().ToLowerInvariant(), allowance, token));
        }

        var activeHolders = holders.Select(h => h.Id).ToHashSet();
        var tokens = (await store.TokensOfSchool(school.Id, ct)).Select(t => new TillTokenBlock(t.TokenHash, t.Status.ToString().ToLowerInvariant())).ToList();
        var restrictions = (await store.RestrictionsOfSchool(school.Id, ct)).Select(r => new TillRestriction(r.WalletId, r.Kind.ToString().ToLowerInvariant(), r.Key)).ToList();

        var names = holders.ToDictionary(h => h.Id);
        var preOrders = (await store.PreOrdersOf(school.Id, day, ct)).Where(p => p.Status is PreOrderStatus.Placed or PreOrderStatus.Ready && activeHolders.Contains(p.HolderId))
            .Select(p => PreOrderMapping.ToTill(p, names[p.HolderId])).ToList();

        return Result<TillSnapshot>.Ok(new TillSnapshot(day, rows, tokens, restrictions, preOrders));
    }
}

// ---------------------------------------------------------------- W3-2: settling what the tills sold without a connection

public sealed record SettleOfflineSales(Guid ShopId, SettleOfflineSalesRequest Request) : ICommand<SettleOfflineSalesResult>;

internal sealed class SettleOfflineSalesHandler(
    IWalletStore store, ITillAccessChecker tills, PurchasePoster poster, PurchaseAlerts alerts, IWalletNotifier notifier, WalletKeys keys, TimeProvider clock)
    : ICommandHandler<SettleOfflineSales, SettleOfflineSalesResult>
{
    public const int MaxSales = 500;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public async Task<Result<SettleOfflineSalesResult>> Handle(SettleOfflineSales c, CancellationToken ct)
    {
        var r = c.Request;
        var access = await tills.Check(c.ShopId, r.DeviceId, r.StaffId, "sale.create", ct);
        if (!access.IsSuccess) return Result<SettleOfflineSalesResult>.Fail(access.Error!.Code, access.Error.Message);
        var school = await store.SchoolByShop(c.ShopId, ct);
        if (school is null) return Result<SettleOfflineSalesResult>.Fail("wallet.no_school", "This shop does not take wallet payments.");
        if (r.Sales is null || r.Sales.Count == 0) return Result<SettleOfflineSalesResult>.Ok(new([]));
        if (r.Sales.Count > MaxSales) return Result<SettleOfflineSalesResult>.Fail("sync.too_large", $"Send at most {MaxSales} sales at a time.");

        var now = clock.GetUtcNow();
        var outcomes = new List<OfflineSaleOutcome>();
        var afterwards = new List<Func<Task>>();

        foreach (var sale in r.Sales)
        {
            // A sale already settled is answered the same way again, whatever the allowance says now.
            var earlier = await store.GetOfflineRecord(sale.SaleId, ct);
            if (earlier is not null)
            {
                outcomes.Add(new(sale.SaleId, earlier.Status.ToString().ToLowerInvariant(), earlier.Reason, earlier.Overdrawn));
                continue;
            }

            var rejected = await Check(school, sale, now, ct);
            if (rejected is not null)
            {
                await store.Add(new OfflineSaleRecord(sale.SaleId, sale.WalletId, r.DeviceId, sale.AmountCents, OfflineSaleStatus.Rejected, rejected, false, sale.SoldAt, now), ct);
                outcomes.Add(new(sale.SaleId, "rejected", rejected, false));
                var rejectedSale = sale;
                afterwards.Add(() => TellAboutRejection(school, rejectedSale, rejected, ct));
                continue;
            }

            var wallet = (await store.GetWallet(sale.WalletId, ct))!;
            var holder = (await store.GetHolder(wallet.HolderId, ct))!;
            var posted = await poster.Post(school, holder, wallet, sale.SaleId, sale.AmountCents, PurchasePoster.ToItems(sale.Items), r.DeviceId, r.StaffId, sale.SoldAt, offline: true, staffAccount: false, ct);
            if (!posted.IsSuccess) return Result<SettleOfflineSalesResult>.Fail(posted.Error!.Code, posted.Error.Message);

            var p = posted.Value!;
            await store.Add(new OfflineSaleRecord(sale.SaleId, sale.WalletId, r.DeviceId, sale.AmountCents, OfflineSaleStatus.Settled, null, p.Overdrawn, sale.SoldAt, now), ct);
            // Saved sale by sale, so the next sale on the same wallet sees this one in its balance and in the day's allowance.
            // Already-settled sales are answered from their record, so sending the whole batch again after a failure is safe.
            try { await store.Save(ct); }
            catch (ConcurrencyConflictException) { return Result<SettleOfflineSalesResult>.Fail("sync.busy", "Some wallets were in use at the same moment. Send the same sales again."); }
            outcomes.Add(new(sale.SaleId, "settled", p.Overdrawn ? "wallet.overdrawn" : null, p.Overdrawn));
            if (!p.Repeated)
            {
                var done = sale;
                afterwards.Add(async () =>
                {
                    await alerts.After(holder, wallet, done.AmountCents, p.BalanceBefore, p.BalanceAfter, done.SaleId, now, ct);
                    if (p.Overdrawn) await TellAboutOverdraw(holder, done, ct);
                });
            }
        }

        try { await store.Save(ct); }
        catch (ConcurrencyConflictException) { return Result<SettleOfflineSalesResult>.Fail("sync.busy", "Some wallets were in use at the same moment. Send the same sales again."); }
        foreach (var a in afterwards) await a();
        return Result<SettleOfflineSalesResult>.Ok(new(outcomes));
    }

    /// <summary>The reason the sale cannot be honoured, or null if it can. The shop is protected by the signed allowance, so this checks only what the allowance promises.</summary>
    private async Task<string?> Check(School school, OfflineWalletSale sale, DateTimeOffset now, CancellationToken ct)
    {
        if (sale.AmountCents is <= 0 or > SpendRules.MaxPurchaseCents) return "sale.amount_invalid";
        if (sale.SoldAt > now + ClockSkew || now - sale.SoldAt > MaxAge) return "sale.date_invalid";

        var allowance = Allowances.Verify(keys.AllowanceKey, sale.AllowanceToken);
        if (allowance is null) return "allowance.invalid";
        if (allowance.WalletId != sale.WalletId) return "allowance.wrong_wallet";
        var day = BusinessDay.Of(sale.SoldAt);
        if (allowance.Day != day) return "allowance.wrong_day";

        var wallet = await store.GetWallet(sale.WalletId, ct);
        if (wallet is null || wallet.SchoolId != school.Id) return "wallet.unknown";

        // Across every till, the day's offline sales on this wallet can never add up to more than the allowance that was signed.
        var settled = await store.OfflineSettledBetween(wallet.Id, BusinessDay.Start(day), BusinessDay.Start(day.AddDays(1)), ct);
        return settled + sale.AmountCents > allowance.TotalCents ? "allowance.exceeded" : null;
    }

    private async Task TellAboutRejection(School school, OfflineWalletSale sale, string reason, CancellationToken ct)
    {
        try
        {
            await notifier.ToShopOwner(school.ShopId, "wallet.sale_rejected", new() { ["amount"] = Rands.Rand(sale.AmountCents), ["reason"] = reason, ["sold"] = sale.SoldAt.ToOffset(BusinessDay.Offset).ToString("yyyy-MM-dd HH:mm") }, $"wallet:rejected:{sale.SaleId:N}:shop", ct);
            var wallet = await store.GetWallet(sale.WalletId, ct);
            if (wallet is null || wallet.SchoolId != school.Id) return;
            var holder = (await store.GetHolder(wallet.HolderId, ct))!;
            foreach (var g in await store.GuardiansOf(holder.Id, ct))
                await notifier.ToParent(g.ParentPersonId, "wallet.sale_rejected", new() { ["amount"] = Rands.Rand(sale.AmountCents), ["reason"] = reason, ["child"] = holder.FirstName },
                    $"wallet:rejected:{sale.SaleId:N}:{g.ParentPersonId:N}", whatsapp: false, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { }
    }

    private async Task TellAboutOverdraw(Holder holder, OfflineWalletSale sale, CancellationToken ct)
    {
        try
        {
            foreach (var g in await store.GuardiansOf(holder.Id, ct))
                await notifier.ToParent(g.ParentPersonId, "wallet.overdrawn", new() { ["child"] = holder.FirstName, ["amount"] = Rands.Rand(sale.AmountCents) },
                    $"wallet:overdrawn:{sale.SaleId:N}:{g.ParentPersonId:N}", whatsapp: true, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { }
    }
}

internal static class PreOrderMapping
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static List<PreOrderLineDto> Lines(PreOrder p) =>
        JsonSerializer.Deserialize<List<PreOrderLineDto>>(p.LinesJson, Json) ?? [];

    public static TillPreOrder ToTill(PreOrder p, Holder holder) =>
        new(p.Id, holder.Id, holder.DisplayName, holder.ClassName, p.BreakCode, p.CollectOn, p.Status.ToString().ToLowerInvariant(), Lines(p));

    public static PreOrderResponse ToResponse(PreOrder p) =>
        new(p.Id, p.HolderId, p.BreakCode, p.CollectOn, p.Status.ToString().ToLowerInvariant(), p.TotalCents, p.CutOffAt, Lines(p));

    public static string Write(IEnumerable<PreOrderLineDto> lines) => JsonSerializer.Serialize(lines, Json);
}
