using System.Globalization;
using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;
using TillKnowIT.Storage;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

// ---------------------------------------------------------------- W5-2: unused money back when a child leaves

public sealed record RequestRefund(Guid WalletId, RefundRequestDto Request) : ICommand<RefundResponse>;
public sealed record ReviewRefund(Guid RefundId, ReviewRefundRequest Request) : ICommand<RefundResponse>;

internal sealed class RefundHandlers(IWalletStore store, IStoredValuePartner partner, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<RequestRefund, RefundResponse>, ICommandHandler<ReviewRefund, RefundResponse>
{
    public async Task<Result<RefundResponse>> Handle(RequestRefund c, CancellationToken ct)
    {
        var opened = await WalletAccess.Parent(store, c.WalletId, c.Request.CallerPersonId, ct);
        if (!opened.IsSuccess) return Result<RefundResponse>.Fail(opened.Error!.Code, opened.Error.Message);
        var (wallet, holder, _) = opened.Value;
        if (holder.Status == HolderStatus.Active) return Result<RefundResponse>.Fail("refund.child_still_enrolled", "A refund is for when your child leaves the school. Ask the school to mark them as left.");
        if ((await store.RefundsOf(wallet.Id, ct)).Any(r => r.Status is RefundStatus.Requested or RefundStatus.Approved)) return Result<RefundResponse>.Fail("refund.already_requested", "A refund has already been asked for.");

        var request = RefundRequest.Request(wallet.Id, wallet.SchoolId, c.Request.CallerPersonId, await store.Balance(wallet.AccountId, ct), clock.GetUtcNow());
        if (!request.IsSuccess) return Result<RefundResponse>.Fail(request.Error!.Code, request.Error.Message);
        await store.Add(request.Value!, ct);
        await store.Save(ct);
        return Result<RefundResponse>.Ok(ToResponse(request.Value!));
    }

    /// <summary>The school confirms the child has left; the partner pays the parent back; the ledger records it. Money first, so a failed payout changes nothing.</summary>
    public async Task<Result<RefundResponse>> Handle(ReviewRefund c, CancellationToken ct)
    {
        var request = await store.GetRefund(c.RefundId, ct);
        if (request is null) return Result<RefundResponse>.Fail("refund.not_found", "Refund request not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, request.SchoolId, c.Request.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<RefundResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        if (!c.Request.Approve)
        {
            var rejected = request.Reject(c.Request.CallerPersonId);
            if (!rejected.IsSuccess) return Result<RefundResponse>.Fail(rejected.Error!.Code, rejected.Error.Message);
            await store.Save(ct);
            return Result<RefundResponse>.Ok(ToResponse(request));
        }

        var wallet = (await store.GetWallet(request.WalletId, ct))!;
        var balance = await store.Balance(wallet.AccountId, ct);
        var can = request.CanApprove(balance);
        if (!can.IsSuccess) return Result<RefundResponse>.Fail(can.Error!.Code, can.Error.Message);

        // The partner is asked before anything here changes, so a partner that is down leaves the request exactly as it was.
        var paid = await partner.RefundToParent(wallet.Id, Math.Min(request.AmountCents, balance), $"refund:{request.Id:N}", ct);
        if (!paid.IsSuccess) return Result<RefundResponse>.Fail(paid.Error!.Code, paid.Error.Message);
        request.Approve(c.Request.CallerPersonId, balance);

        var txn = LedgerTransaction.Create(LedgerTransactionType.Refund, $"refund:{request.Id:N}", Postings.Refund(wallet.Id, request.AmountCents), clock.GetUtcNow(), paid.Value, "Refund to the parent");
        await store.Add(txn.Value!, ct);
        request.MarkPaid(paid.Value!);
        wallet.NoteDebit();
        if (await store.Balance(wallet.AccountId, ct) - request.AmountCents <= 0) wallet.Close();
        await store.Save(ct);
        return Result<RefundResponse>.Ok(ToResponse(request));
    }

    private static RefundResponse ToResponse(RefundRequest r) => new(r.Id, r.WalletId, r.AmountCents, r.Status.ToString().ToLowerInvariant());
}

// ---------------------------------------------------------------- W5-3: the ledger against the partner, every day

/// <summary>A caller is optional: the worker reconciles every day with none, and a person (a platform admin) can run it by hand.</summary>
public sealed record RunWalletReconciliation(DateOnly Day, Guid? CallerPersonId) : ICommand<ReconciliationResponse>;
public sealed record ResolveWalletReconciliation(Guid ReconciliationId, ResolveReconciliationRequest Request) : ICommand<ReconciliationResponse>;

internal sealed class ReconciliationHandlers(IWalletStore store, IStoredValuePartner partner, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<RunWalletReconciliation, ReconciliationResponse>, ICommandHandler<ResolveWalletReconciliation, ReconciliationResponse>
{
    public async Task<Result<ReconciliationResponse>> Handle(RunWalletReconciliation c, CancellationToken ct)
    {
        if (c.CallerPersonId is { } caller)
        {
            var allowed = await SchoolAccess.Platform(permissions, caller, "wallet.admin", ct);
            if (!allowed.IsSuccess) return Result<ReconciliationResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        }

        // What the partner holds for us is the negative of our partner float account: every rand in, less every rand out.
        var run = WalletReconciliation.Run(c.Day, await partner.HeldTotal(ct), -await store.Balance(AccountIds.PartnerFloat, ct), clock.GetUtcNow());
        var broken = await store.UnbalancedTransactions(5, ct);
        if (broken.Count > 0) run.FlagIntegrity($"{broken.Count} ledger transaction(s) do not add up to zero, first: {broken[0]}");

        await store.Add(run, ct);
        await store.Save(ct);
        return Result<ReconciliationResponse>.Ok(ToResponse(run));
    }

    public async Task<Result<ReconciliationResponse>> Handle(ResolveWalletReconciliation c, CancellationToken ct)
    {
        var allowed = await SchoolAccess.Platform(permissions, c.Request.CallerPersonId, "wallet.admin", ct);
        if (!allowed.IsSuccess) return Result<ReconciliationResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var run = await store.GetReconciliation(c.ReconciliationId, ct);
        if (run is null) return Result<ReconciliationResponse>.Fail("reconciliation.not_found", "Reconciliation not found.");

        var resolved = run.Resolve(c.Request.CallerPersonId, c.Request.Note);
        if (!resolved.IsSuccess) return Result<ReconciliationResponse>.Fail(resolved.Error!.Code, resolved.Error.Message);
        await store.Save(ct);
        return Result<ReconciliationResponse>.Ok(ToResponse(run));
    }

    internal static ReconciliationResponse ToResponse(WalletReconciliation r) =>
        new(r.Id, r.Day, r.PartnerTotalCents, r.LedgerTotalCents, r.DifferenceCents, r.Status.ToString().ToLowerInvariant(), r.Note);
}

// ---------------------------------------------------------------- W5-1: paying the tuckshop

/// <summary>
/// Pays each tuckshop what its wallet sales earned, when its schedule says it is due. A caller is optional: the worker runs it every day with none.
/// An unresolved reconciliation difference blocks every payout until someone has looked at it.
/// </summary>
public sealed record RunPayouts(DateOnly? Day, Guid? CallerPersonId) : ICommand<RunPayoutsResult>;
public sealed record GetShopWalletSales(Guid ShopId, Guid CallerPersonId, DateOnly? From, DateOnly? To) : IQuery<WalletSalesSummary>;

public static class PayoutSchedules
{
    public static bool IsDue(PayoutSchedule schedule, DateOnly day) => schedule switch
    {
        PayoutSchedule.Daily => true,
        PayoutSchedule.Weekly => day.DayOfWeek == DayOfWeek.Friday,
        _ => day.AddDays(1).Day == 1,   // the last day of the month
    };
}

internal sealed class PayoutHandlers(IWalletStore store, IStoredValuePartner partner, IPermissionChecker permissions, IOrganisationAccess access, TimeProvider clock)
    : ICommandHandler<RunPayouts, RunPayoutsResult>, IQueryHandler<GetShopWalletSales, WalletSalesSummary>
{
    public async Task<Result<RunPayoutsResult>> Handle(RunPayouts c, CancellationToken ct)
    {
        if (c.CallerPersonId is { } caller)
        {
            var allowed = await SchoolAccess.Platform(permissions, caller, "wallet.admin", ct);
            if (!allowed.IsSuccess) return Result<RunPayoutsResult>.Fail(allowed.Error!.Code, allowed.Error.Message);
        }
        if (await store.OpenMismatch(ct) is { } mismatch)
            return Result<RunPayoutsResult>.Ok(new(true, $"Payouts are on hold: the reconciliation for {mismatch.Day:yyyy-MM-dd} differs by {mismatch.DifferenceCents} cents and has not been resolved.", []));

        var now = clock.GetUtcNow();
        var day = c.Day ?? BusinessDay.Of(now);
        var done = new List<PayoutResponse>();
        foreach (var school in (await store.AllSchools(ct)).Where(s => s.IsTrading && PayoutSchedules.IsDue(s.PayoutSchedule, day)))
        {
            var owed = await store.Balance(AccountIds.ShopPayable(school.ShopId), ct);
            if (owed <= 0) continue;

            var reference = $"payout:{school.ShopId:N}:{day:yyyyMMdd}";
            if (await store.TransactionExists(reference, ct)) continue;   // already paid today

            var paid = await partner.PayoutToShop(school.ShopId, owed, reference, ct);
            if (!paid.IsSuccess)
            {
                var failed = new ShopPayout(school.ShopId, owed, PayoutStatus.Failed, null, paid.Error!.Message, day, now);
                await store.Add(failed, ct);
                done.Add(ToResponse(failed));
                continue;
            }

            var txn = LedgerTransaction.Create(LedgerTransactionType.Payout, reference, Postings.Payout(school.ShopId, owed), now, paid.Value, "Payout to the tuckshop");
            await store.Add(txn.Value!, ct);
            var payout = new ShopPayout(school.ShopId, owed, PayoutStatus.Paid, paid.Value, null, day, now);
            await store.Add(payout, ct);
            done.Add(ToResponse(payout));
        }

        await store.Save(ct);
        return Result<RunPayoutsResult>.Ok(new(false, null, done));
    }

    /// <summary>What wallet sales earned the shop, what the platform kept and what has been paid out, for the owner.</summary>
    public async Task<Result<WalletSalesSummary>> Handle(GetShopWalletSales q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.ShopId, "report.view", ct)) return Result<WalletSalesSummary>.Fail("permission.denied", "You do not have access to this shop.");

        var toDay = q.To ?? BusinessDay.Of(clock.GetUtcNow());
        var fromDay = q.From ?? toDay.AddDays(-29);
        if (fromDay > toDay) return Result<WalletSalesSummary>.Fail("report.range_invalid", "The start date is after the end date.");

        var purchases = (await store.PurchasesOfShop(q.ShopId, BusinessDay.Start(fromDay), BusinessDay.Start(toDay.AddDays(1)), ct)).Where(p => p.ReversedAt is null).ToList();
        var payouts = (await store.PayoutsOf(q.ShopId, ct)).Where(p => p.Day >= fromDay && p.Day <= toDay).OrderByDescending(p => p.Day).ToList();
        return Result<WalletSalesSummary>.Ok(new WalletSalesSummary(
            purchases.Sum(p => p.AmountCents), purchases.Sum(p => p.FeeCents), payouts.Where(p => p.Status == PayoutStatus.Paid).Sum(p => p.AmountCents),
            await store.Balance(AccountIds.ShopPayable(q.ShopId), ct), payouts.Select(ToResponse).ToList()));
    }

    private static PayoutResponse ToResponse(ShopPayout p) => new(p.Id, p.ShopId, p.AmountCents, p.Status.ToString().ToLowerInvariant(), p.Day, p.Error);
}

// ---------------------------------------------------------------- W5-4: removing a child's data

public sealed record RequestErasure(Guid HolderId, Guid CallerPersonId) : ICommand<ErasureRequestResponse>;
public sealed record ProcessPendingErasures : ICommand<int>;

/// <summary>
/// A parent asks for their child's data to be removed. Anything with money still in it is refunded first (the ledger is kept, as the law requires,
/// but holds no names). When nothing blocks it the removal happens at once; otherwise it waits, and is done within 30 days.
/// </summary>
internal sealed class ErasureHandlers(IWalletStore store, IFileStore files, TimeProvider clock)
    : ICommandHandler<RequestErasure, ErasureRequestResponse>, ICommandHandler<ProcessPendingErasures, int>
{
    public async Task<Result<ErasureRequestResponse>> Handle(RequestErasure c, CancellationToken ct)
    {
        if (await store.GuardianshipOf(c.CallerPersonId, c.HolderId, ct) is null) return Result<ErasureRequestResponse>.Fail("permission.denied", "You are not linked to this child.");
        var request = await store.ErasureOf(c.HolderId, ct);
        if (request is null)
        {
            request = new ErasureRequest(c.HolderId, c.CallerPersonId, clock.GetUtcNow());
            await store.Add(request, ct);
            await store.Save(ct);
        }
        if (request.CompletedAt is null) await TryErase(request, ct);
        return Result<ErasureRequestResponse>.Ok(new(request.Id, request.DueBy, request.CompletedAt is not null));
    }

    public async Task<Result<int>> Handle(ProcessPendingErasures c, CancellationToken ct)
    {
        var done = 0;
        foreach (var request in await store.PendingErasures(ct))
            if (await TryErase(request, ct)) done++;
        return Result<int>.Ok(done);
    }

    private async Task<bool> TryErase(ErasureRequest request, CancellationToken ct)
    {
        var holder = await store.GetHolder(request.HolderId, ct);
        if (holder is null) return false;
        var wallet = await store.WalletOfHolder(holder.Id, ct);

        // Money first: a wallet with money in it cannot disappear. The parent gets it back through a refund, then this completes.
        if (wallet is not null && await store.Balance(wallet.AccountId, ct) != 0) return false;

        var now = clock.GetUtcNow();
        var photo = holder.PhotoUrl;
        foreach (var token in await store.TokensOf(holder.Id, ct)) token.Block(now);
        holder.Erase();
        wallet?.Close();
        await store.RemoveGuardianships(holder.Id, ct);
        request.Complete(now);
        await store.Save(ct);
        if (photo is not null) await files.Delete(photo, ct);
        return true;
    }
}

// ---------------------------------------------------------------- W4-1: teachers' monthly staff accounts

public sealed record GetStaffMonth(Guid SchoolId, Guid CallerPersonId, string Month) : IQuery<StaffMonthStatement>;
public sealed record SettleStaffMonth(Guid SchoolId, Guid CallerPersonId, string Month) : ICommand<StaffMonthStatement>;

internal sealed class StaffAccountHandlers(IWalletStore store, IPermissionChecker permissions, TimeProvider clock)
    : IQueryHandler<GetStaffMonth, StaffMonthStatement>, ICommandHandler<SettleStaffMonth, StaffMonthStatement>
{
    public async Task<Result<StaffMonthStatement>> Handle(GetStaffMonth q, CancellationToken ct)
    {
        var allowed = await SchoolAccess.Admin(store, permissions, q.SchoolId, q.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<StaffMonthStatement>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return await Build(q.SchoolId, q.Month, ct);
    }

    /// <summary>The school has paid its teachers' month; the platform clears the staff accounts. Recorded once per teacher and month.</summary>
    public async Task<Result<StaffMonthStatement>> Handle(SettleStaffMonth c, CancellationToken ct)
    {
        var allowed = await SchoolAccess.Platform(permissions, c.CallerPersonId, "wallet.admin", ct);
        if (!allowed.IsSuccess) return Result<StaffMonthStatement>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var statement = await Build(c.SchoolId, c.Month, ct);
        if (!statement.IsSuccess) return statement;

        var now = clock.GetUtcNow();
        foreach (var line in statement.Value!.Teachers.Where(t => t.OwedCents > 0))
        {
            var reference = $"staffsettle:{line.HolderId:N}:{c.Month}";
            if (await store.TransactionExists(reference, ct)) continue;
            var txn = LedgerTransaction.Create(LedgerTransactionType.StaffSettlement, reference, Postings.StaffSettlement(line.HolderId, line.OwedCents), now, description: $"Staff account for {c.Month}");
            await store.Add(txn.Value!, ct);
        }
        await store.Save(ct);
        return statement;
    }

    private async Task<Result<StaffMonthStatement>> Build(Guid schoolId, string month, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
            return Result<StaffMonthStatement>.Fail("staff.month_invalid", "The month is written like 2026-10.");
        if (await store.GetSchool(schoolId, ct) is null) return Result<StaffMonthStatement>.Fail("school.not_found", "School not found.");

        var from = BusinessDay.Start(first);
        var to = BusinessDay.Start(first.AddMonths(1));
        var lines = new List<StaffMonthLine>();
        foreach (var teacher in (await store.HoldersOf(schoolId, ct)).Where(h => h.Type == HolderType.Teacher && h.Status != HolderStatus.Erased))
        {
            // What the teacher bought on the staff account in the month, less anything already settled in it.
            var owed = -(await store.EntriesOf(AccountIds.StaffAccount(teacher.Id), from, to, ct)).Sum(e => e.AmountCents);
            if (owed != 0) lines.Add(new StaffMonthLine(teacher.Id, teacher.DisplayName, owed));
        }
        return Result<StaffMonthStatement>.Ok(new StaffMonthStatement(month, lines.Sum(l => Math.Max(0, l.OwedCents)), lines.OrderBy(l => l.Name).ToList()));
    }
}
