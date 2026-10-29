using TillKnowIT.SharedKernel;

namespace ShopCore.Domain.Selling;

/// <summary>Payment methods that only exist with the Phase 4 packs. The Phase 1 four stay in <see cref="PaymentMethods"/>.</summary>
public static class ExtraPaymentMethods
{
    public const string Wallet = "wallet";
    public const string Voucher = "voucher";
    public const string Points = "points";
    public const string Layby = "layby";
    public const string Split = "split";

    /// <summary>What one part of a split payment can be: not "split" or "layby", which describe a whole sale.</summary>
    public static bool IsPart(string? code) => code is PaymentMethods.Cash or PaymentMethods.Card or PaymentMethods.Qr or PaymentMethods.Credit or Wallet or Voucher or Points;
}

/// <summary>
/// One part of a split payment. Reference is the wallet purchase id for a wallet part, or the voucher code for a voucher part.
/// AccountId is the credit account for a credit part.
/// </summary>
public sealed record PaymentPart(string MethodCode, long AmountCents, string? Reference = null, Guid? AccountId = null, long? TenderedCents = null);

public static class SplitPayments
{
    public const int MaxParts = 6;

    /// <summary>
    /// B2-5: the parts must add up to exactly what is owed (the sale plus any tip). Each method is used at most once. Only cash may be tendered
    /// above its part, and then the change is worked out here, not taken from the till. A credit part needs an account, a wallet or voucher part
    /// needs its reference.
    /// </summary>
    public static Result<long> Check(IReadOnlyList<PaymentPart> parts, long owedCents)
    {
        if (parts.Count == 0) return Result<long>.Fail("payment.parts_required", "Say how the sale was paid.");
        if (parts.Count > MaxParts) return Result<long>.Fail("payment.too_many_parts", $"A sale can be split into at most {MaxParts} payments.");
        if (parts.Any(p => !ExtraPaymentMethods.IsPart(p.MethodCode))) return Result<long>.Fail("payment.method_invalid", "Unknown payment method.");
        if (parts.Select(p => p.MethodCode).Distinct().Count() != parts.Count) return Result<long>.Fail("payment.method_repeated", "Each payment method can be used once.");
        if (parts.Any(p => p.AmountCents <= 0)) return Result<long>.Fail("payment.amount_invalid", "Every payment must be more than zero.");
        if (parts.Sum(p => p.AmountCents) != owedCents) return Result<long>.Fail("payment.total_mismatch", "The payments do not add up to what is owed.");

        foreach (var p in parts)
        {
            if (p.MethodCode == PaymentMethods.Credit && p.AccountId is null) return Result<long>.Fail("payment.account_required", "A credit payment needs a credit account.");
            if (p.MethodCode != PaymentMethods.Credit && p.AccountId is not null) return Result<long>.Fail("payment.account_unexpected", "Only a credit payment has a credit account.");
            if (p.MethodCode is ExtraPaymentMethods.Wallet or ExtraPaymentMethods.Voucher or ExtraPaymentMethods.Points && string.IsNullOrWhiteSpace(p.Reference)) return Result<long>.Fail("payment.reference_required", $"A {p.MethodCode} payment needs its reference.");
            if (p.MethodCode == PaymentMethods.Cash)
            {
                if (p.TenderedCents is { } t && t < p.AmountCents) return Result<long>.Fail("payment.tender_invalid", "The cash handed over is less than the cash payment.");
            }
            else if (p.TenderedCents is not null) return Result<long>.Fail("payment.tender_invalid", "Only a cash payment has an amount handed over.");
        }

        var cash = parts.FirstOrDefault(p => p.MethodCode == PaymentMethods.Cash);
        return Result<long>.Ok(cash?.TenderedCents is { } tendered ? tendered - cash.AmountCents : 0);
    }
}

public static class Tips
{
    public const int MaxPercent = 100;

    /// <summary>B3-3: a tip by amount, or by percentage of the subtotal rounded half up. Never negative and never more than the sale itself.</summary>
    public static Result<long> FromAmount(long tipCents, long subtotalCents) =>
        tipCents < 0 || tipCents > Math.Max(0, subtotalCents) ? Result<long>.Fail("tip.invalid", "A tip cannot be negative or more than the bill.") : Result<long>.Ok(tipCents);

    public static Result<long> FromPercent(int percent, long subtotalCents) =>
        percent is < 0 or > MaxPercent ? Result<long>.Fail("tip.invalid", "A tip percentage is from 0 to 100.") : Result<long>.Ok((subtotalCents * percent + 50) / 100);
}

/// <summary>What was sold on a line: its quantity and what it cost the customer in total (after any discount), so a refund is pro rata.</summary>
public sealed record SoldLine(Guid SaleLineId, Guid ProductId, string Name, int Quantity, long LineTotalCents);

public sealed record RefundLineInput(Guid SaleLineId, int Quantity);

/// <summary>What has already been given back on a sale line.</summary>
public sealed record RefundedSoFar(Guid SaleLineId, int Quantity);

public sealed record RefundPlan(long AmountCents, IReadOnlyList<(Guid SaleLineId, Guid ProductId, int Quantity, long AmountCents)> Lines, bool NeedsApproval);

public static class RefundRules
{
    /// <summary>The refund above which a manager must approve, in cents, when the shop has not set its own.</summary>
    public const long DefaultApprovalThresholdCents = 50_000;

    /// <summary>
    /// B2-4: works out what to give back. Each line may be refunded up to what was sold less what has already been refunded, so a sale can never
    /// be refunded for more than it was. The amount is the share of what the customer actually paid for the line (after any promotion) that the quantity is,
    /// rounded half up, so refunding the whole line gives back exactly what it cost.
    /// </summary>
    public static Result<RefundPlan> Plan(IReadOnlyList<SoldLine> sold, IReadOnlyList<RefundedSoFar> already, IReadOnlyList<RefundLineInput> asked, long approvalThresholdCents)
    {
        if (asked.Count == 0) return Result<RefundPlan>.Fail("refund.no_lines", "Choose what is being refunded.");
        if (asked.Select(a => a.SaleLineId).Distinct().Count() != asked.Count) return Result<RefundPlan>.Fail("refund.line_repeated", "List each item once, with its quantity.");

        var lines = new List<(Guid, Guid, int, long)>();
        foreach (var a in asked)
        {
            var line = sold.FirstOrDefault(s => s.SaleLineId == a.SaleLineId);
            if (line is null) return Result<RefundPlan>.Fail("refund.line_unknown", "That item was not part of the sale.");
            if (a.Quantity < 1) return Result<RefundPlan>.Fail("refund.quantity_invalid", "The quantity must be more than zero.");
            var left = line.Quantity - already.Where(r => r.SaleLineId == line.SaleLineId).Sum(r => r.Quantity);
            if (a.Quantity > left) return Result<RefundPlan>.Fail("refund.too_many", $"{line.Name}: only {Math.Max(0, left)} left to refund.");
            lines.Add((line.SaleLineId, line.ProductId, a.Quantity, (line.LineTotalCents * a.Quantity + line.Quantity / 2) / line.Quantity));
        }
        var total = lines.Sum(l => l.Item4);
        return Result<RefundPlan>.Ok(new RefundPlan(total, lines, total > approvalThresholdCents));
    }

    /// <summary>
    /// A refund goes back the way the money came. Cash and card go back the same way; a credit sale reduces what the customer owes;
    /// anything else (a wallet, a voucher, a lay-by) is returned as a gift voucher, since the original method cannot be paid back at the counter.
    /// </summary>
    public static string RefundMethodFor(string originalMethodCode) => originalMethodCode switch
    {
        PaymentMethods.Cash => PaymentMethods.Cash,
        PaymentMethods.Card => PaymentMethods.Card,
        PaymentMethods.Qr => PaymentMethods.Qr,
        PaymentMethods.Credit => PaymentMethods.Credit,
        _ => ExtraPaymentMethods.Voucher,
    };
}
