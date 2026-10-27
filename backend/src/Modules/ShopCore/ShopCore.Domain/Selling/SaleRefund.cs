using TillKnowIT.SharedKernel;

namespace ShopCore.Domain.Selling;

public sealed record SaleRefundInput(
    Guid Id, Guid ShopId, Guid SaleId, Guid StaffId, Guid DeviceId, string Reason, string MethodCode, long AmountCents,
    Guid? ApprovedBy, IReadOnlyList<(Guid ProductId, int Quantity, long AmountCents)> Lines, DateTimeOffset RefundedAt);

/// <summary>
/// B2-4: goods taken back from a past sale. Append only and always tied to the sale it came from. Stock goes back (a Return movement per line)
/// and the money goes back the way it came; both are recorded alongside this by whatever processes the refund.
/// </summary>
public sealed class SaleRefund : Entity
{
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromHours(24);

    private SaleRefund() { }

    public static Result<SaleRefund> Record(SaleRefundInput i, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(i.Reason) || i.Reason.Trim().Length > 200) return Result<SaleRefund>.Fail("refund.reason_required", "Say why the items are being returned (up to 200 characters).");
        if (i.Lines.Count == 0) return Result<SaleRefund>.Fail("refund.no_lines", "Choose what is being refunded.");
        if (i.Lines.Sum(l => l.AmountCents) != i.AmountCents || i.AmountCents <= 0) return Result<SaleRefund>.Fail("refund.amount_mismatch", "The refund does not match its items.");
        if (i.RefundedAt > now + MaxClockSkew) return Result<SaleRefund>.Fail("refund.date_invalid", "The refund is dated in the future.");

        var refund = new SaleRefund
        {
            ShopId = i.ShopId, SaleId = i.SaleId, Reason = i.Reason.Trim(), MethodCode = i.MethodCode, AmountCents = i.AmountCents, ApprovedBy = i.ApprovedBy,
            RefundedAt = i.RefundedAt, Lines = i.Lines.Select(l => new SaleRefundLine(i.Id, l.ProductId, l.Quantity, l.AmountCents)).ToList(),
        };
        refund.StampFromDevice(i.Id, i.RefundedAt, i.StaffId, i.DeviceId);
        return Result<SaleRefund>.Ok(refund);
    }

    public Guid ShopId { get; private set; }
    public Guid SaleId { get; private set; }
    public string Reason { get; private set; } = default!;

    /// <summary>cash, card, qr, credit or voucher: how the customer was paid back.</summary>
    public string MethodCode { get; private set; } = default!;

    public long AmountCents { get; private set; }

    /// <summary>The manager who approved a refund above the threshold.</summary>
    public Guid? ApprovedBy { get; private set; }

    public DateTimeOffset RefundedAt { get; private set; }
    public List<SaleRefundLine> Lines { get; private set; } = [];
}

public sealed class SaleRefundLine
{
    private SaleRefundLine() { }

    public SaleRefundLine(Guid refundId, Guid productId, int quantity, long amountCents)
    {
        RefundId = refundId;
        ProductId = productId;
        Quantity = quantity;
        AmountCents = amountCents;
    }

    public Guid RefundId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public long AmountCents { get; private set; }
}
