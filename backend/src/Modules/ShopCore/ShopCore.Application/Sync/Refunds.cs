using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Sync;

/// <summary>
/// Asks whether a feature (split payments, refunds, tips, customers, and so on) is on for a shop, and for its settings. The sale flow asks this and nothing
/// else: it never learns which business pack a shop is on. When no gate is registered every feature is on.
/// </summary>
public interface IFeatureGate
{
    Task<bool> IsOn(Guid shopId, string feature, CancellationToken ct);

    /// <summary>The settings of a feature as JSON, or null if it has none.</summary>
    Task<string?> Settings(Guid shopId, string feature, CancellationToken ct);
}

/// <summary>The records a refund adds besides itself: the goods back in stock and, for a credit sale, the debt reduced.</summary>
public interface IRefundEffects
{
    void Add(CreditEntry entry);
    void Add(StockMovement movement);
}

/// <summary>What the refund processor needs from the database, beyond what an upload batch already has.</summary>
public interface IRefundStore
{
    Task<HashSet<Guid>> ExistingIds(IEnumerable<Guid> ids, CancellationToken ct);
    Task<Dictionary<Guid, List<SoldLine>>> SoldLines(Guid shopId, IEnumerable<Guid> saleIds, CancellationToken ct);
    Task<List<(Guid SaleId, Guid ProductId, int Quantity)>> RefundedSoFar(IEnumerable<Guid> saleIds, CancellationToken ct);

    /// <summary>Cash given back at this till in the window, as a positive total.</summary>
    Task<long> CashRefunds(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Cash taken at this till in the window that is not a sale: lay-by payments and gift vouchers paid for in cash.</summary>
    Task<long> CashIn(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    void Add(SaleRefund refund);
}

/// <summary>What a refund came to, so the caller can tell the till and keep the cash-up right.</summary>
public sealed record RefundOutcome(SaleRefund Refund, string MethodCode, bool NeededApproval);

/// <summary>
/// B2-4: turns a refund request into the records it needs: the refund itself, the goods back in stock, and, for a credit sale, the debt reduced.
/// Shared by the sync path (a till uploads a refund it made) and the online path, so there is one set of rules.
/// </summary>
public sealed class RefundProcessor(IRefundEffects store, IRefundStore refunds)
{
    public sealed record Context(
        Guid ShopId, Guid DeviceId, Guid ShopStaffId, SaleSummary Sale, IReadOnlyList<SoldLine> SoldLines, IReadOnlyList<RefundedSoFar> Already,
        long ApprovalThresholdCents, bool OnlineAllowed);

    public async Task<Result<RefundOutcome>> Process(RefundUpload r, Context c, Func<Guid, string, Task<Result>> allowed, DateTimeOffset now, CancellationToken ct)
    {
        var plan = RefundRules.Plan(c.SoldLines, c.Already, r.Lines.Select(l => new RefundLineInput(l.ProductId, l.Quantity)).ToList(), c.ApprovalThresholdCents);
        if (!plan.IsSuccess) return Result<RefundOutcome>.Fail(plan.Error!.Code, plan.Error.Message);

        var may = await allowed(r.StaffId, "sale.refund");
        if (!may.IsSuccess) return Result<RefundOutcome>.Fail(may.Error!.Code, may.Error.Message);
        if (plan.Value!.NeedsApproval)
        {
            if (r.ApprovedBy is not { } manager) return Result<RefundOutcome>.Fail("refund.approval_required", "A refund this size needs a manager to approve it.");
            var approver = await allowed(manager, "sale.refund_approve");
            if (!approver.IsSuccess) return Result<RefundOutcome>.Fail("refund.approver_not_allowed", "The person who approved this refund may not do that.");
        }

        // The money goes back the way it came. A wallet, voucher or lay-by sale is refunded online, where a voucher can be given to the customer.
        var method = string.IsNullOrWhiteSpace(r.MethodCode) ? RefundRules.RefundMethodFor(c.Sale.PaymentMethodCode) : r.MethodCode.Trim().ToLowerInvariant();
        var original = RefundRules.RefundMethodFor(c.Sale.PaymentMethodCode);
        if (original == ExtraPaymentMethods.Voucher && !c.OnlineAllowed)
            return Result<RefundOutcome>.Fail("refund.online_only", "This sale was not paid in cash, card, QR or credit, so it has to be refunded online.");
        if (original == PaymentMethods.Credit && method != PaymentMethods.Credit) return Result<RefundOutcome>.Fail("refund.method_invalid", "A credit sale is refunded by reducing what the customer owes.");
        if (original != PaymentMethods.Credit && method == PaymentMethods.Credit) return Result<RefundOutcome>.Fail("refund.method_invalid", "Only a credit sale can be refunded to the credit account.");
        if (method is not (PaymentMethods.Cash or PaymentMethods.Card or PaymentMethods.Qr or PaymentMethods.Credit or ExtraPaymentMethods.Voucher))
            return Result<RefundOutcome>.Fail("refund.method_invalid", "Choose cash, card, QR, credit or voucher.");
        if (method == ExtraPaymentMethods.Voucher && !c.OnlineAllowed) return Result<RefundOutcome>.Fail("refund.online_only", "A voucher has to be issued online.");

        var recorded = SaleRefund.Record(new SaleRefundInput(r.Id, c.ShopId, r.SaleId, r.StaffId, c.DeviceId, r.Reason, method, plan.Value.AmountCents, r.ApprovedBy,
            plan.Value.Lines.Select(l => (l.ProductId, l.Quantity, l.AmountCents)).ToList(), r.RefundedAt, c.Sale.TotalCents), now);
        if (!recorded.IsSuccess) return Result<RefundOutcome>.Fail(recorded.Error!.Code, recorded.Error.Message);

        refunds.Add(recorded.Value!);
        foreach (var line in plan.Value.Lines)
        {
            var movement = StockMovement.Record(new StockMovementInput(Guid.CreateVersion7(), c.ShopId, line.ProductId, StockMovementType.Return, line.Quantity, r.StaffId,
                c.DeviceId, r.RefundedAt, r.SaleId, null, "refund", null, null));
            if (!movement.IsSuccess) return Result<RefundOutcome>.Fail(movement.Error!.Code, movement.Error.Message);
            store.Add(movement.Value!);
        }

        if (method == PaymentMethods.Credit && c.Sale.AccountId is { } account)
        {
            var entry = CreditEntry.Record(new CreditEntryInput(Guid.CreateVersion7(), account, CreditEntryType.Refund, -plan.Value.AmountCents, r.SaleId, null, r.StaffId, c.DeviceId, r.RefundedAt));
            if (!entry.IsSuccess) return Result<RefundOutcome>.Fail(entry.Error!.Code, entry.Error.Message);
            store.Add(entry.Value!);
        }
        return Result<RefundOutcome>.Ok(new RefundOutcome(recorded.Value!, method, plan.Value.NeedsApproval));
    }
}
