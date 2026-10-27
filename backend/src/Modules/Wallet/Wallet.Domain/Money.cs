using TillKnowIT.SharedKernel;

namespace Wallet.Domain;

// ---------------------------------------------------------------- W4: pre-orders

/// <summary>A break in the school day (first break, lunch) with the time orders for it close. Cut-off is a local time on the day of collection.</summary>
public sealed class BreakSlot : Entity
{
    private BreakSlot() { }

    public BreakSlot(Guid schoolId, string code, string name, TimeOnly cutOff, DateTimeOffset now)
    {
        SchoolId = schoolId;
        Code = code.Trim().ToLowerInvariant();
        Name = name.Trim();
        CutOff = cutOff;
        CreatedAt = now;
    }

    public Guid SchoolId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public TimeOnly CutOff { get; private set; }
}

/// <summary>What the tuckshop offers for pre-order.</summary>
public sealed class MenuItem : VersionedEntity
{
    private MenuItem() { }

    public MenuItem(Guid schoolId, string name, long priceCents, string? productId, string? categoryCode, DateTimeOffset now)
    {
        SchoolId = schoolId;
        Name = name.Trim();
        PriceCents = priceCents;
        ProductId = productId;
        CategoryCode = categoryCode;
        IsAvailable = true;
        CreatedAt = now;
    }

    public Guid SchoolId { get; private set; }
    public string Name { get; private set; } = default!;
    public long PriceCents { get; private set; }
    public string? ProductId { get; private set; }
    public string? CategoryCode { get; private set; }
    public bool IsAvailable { get; private set; }

    public static Result Validate(string? name, long priceCents) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 ? Result.Fail("menu.name_invalid", "Give the item a name of up to 100 characters.")
        : priceCents is <= 0 or > 100_000 ? Result.Fail("menu.price_invalid", "The price must be more than zero and at most R1000.")
        : Result.Ok();

    public void Change(string name, long priceCents, bool available)
    {
        Name = name.Trim();
        PriceCents = priceCents;
        IsAvailable = available;
        Touch();
    }
}

public enum PreOrderStatus { Placed, Ready, Collected, Cancelled }

public sealed record PreOrderLine(Guid MenuItemId, string Name, int Quantity, long PriceCents);

/// <summary>W4-2 and W4-3: lunch ordered ahead and paid from the wallet when it is placed. Cancellable until the cut-off; the till only marks it collected.</summary>
public sealed class PreOrder : VersionedEntity
{
    public const int MaxDaysAhead = 7;

    private PreOrder() { }

    public Guid HolderId { get; private set; }
    public Guid WalletId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid ShopId { get; private set; }
    public string BreakCode { get; private set; } = default!;
    public DateOnly CollectOn { get; private set; }
    public DateTimeOffset CutOffAt { get; private set; }
    public string LinesJson { get; private set; } = "[]";
    public long TotalCents { get; private set; }
    public PreOrderStatus Status { get; private set; }
    public Guid PurchaseId { get; private set; }

    /// <summary>The cut-off as a moment: the break's local time on the collection day, in South African time.</summary>
    public static DateTimeOffset CutOffMoment(DateOnly collectOn, TimeOnly cutOff) => new(collectOn.ToDateTime(cutOff), BusinessDay.Offset);

    public static Result<PreOrder> Place(Guid holderId, Guid walletId, Guid schoolId, Guid shopId, BreakSlot slot, DateOnly collectOn, string linesJson, long totalCents, Guid purchaseId, DateTimeOffset now)
    {
        var today = BusinessDay.Of(now);
        if (collectOn < today || collectOn.DayNumber - today.DayNumber > MaxDaysAhead) return Result<PreOrder>.Fail("preorder.date_invalid", "Choose a day from today up to a week ahead.");
        if (collectOn.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return Result<PreOrder>.Fail("preorder.not_a_school_day", "Choose a weekday.");
        var cutOff = CutOffMoment(collectOn, slot.CutOff);
        if (now >= cutOff) return Result<PreOrder>.Fail("preorder.cut_off_passed", $"Orders for {slot.Name} closed at {slot.CutOff:HH\\:mm}.");
        return Result<PreOrder>.Ok(new PreOrder
        {
            HolderId = holderId, WalletId = walletId, SchoolId = schoolId, ShopId = shopId, BreakCode = slot.Code, CollectOn = collectOn, CutOffAt = cutOff,
            LinesJson = linesJson, TotalCents = totalCents, Status = PreOrderStatus.Placed, PurchaseId = purchaseId, CreatedAt = now,
        });
    }

    public Result Cancel(DateTimeOffset now)
    {
        if (Status is PreOrderStatus.Collected or PreOrderStatus.Cancelled) return Result.Fail("preorder.not_cancellable", "This order can no longer be cancelled.");
        if (now >= CutOffAt) return Result.Fail("preorder.cut_off_passed", "The cut-off has passed, so this order can no longer be cancelled.");
        Status = PreOrderStatus.Cancelled;
        Touch();
        return Result.Ok();
    }

    public Result MarkReady()
    {
        if (Status != PreOrderStatus.Placed) return Result.Fail("preorder.not_placed", "Only a placed order can be made ready.");
        Status = PreOrderStatus.Ready;
        Touch();
        return Result.Ok();
    }

    public Result Collect()
    {
        if (Status is not (PreOrderStatus.Placed or PreOrderStatus.Ready)) return Result.Fail("preorder.not_collectable", Status == PreOrderStatus.Collected ? "Already collected." : "This order was cancelled.");
        Status = PreOrderStatus.Collected;
        Touch();
        return Result.Ok();
    }
}

// ---------------------------------------------------------------- W5: refunds, payouts, reconciliation

public enum RefundStatus { Requested, Approved, Paid, Rejected }

/// <summary>W5-2: a parent asks for what is left in the wallet back when the child leaves. The school confirms; the partner pays it out.</summary>
public sealed class RefundRequest : VersionedEntity
{
    private RefundRequest() { }

    public Guid WalletId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public long AmountCents { get; private set; }
    public RefundStatus Status { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public string? PartnerRef { get; private set; }

    public static Result<RefundRequest> Request(Guid walletId, Guid schoolId, Guid parent, long balanceCents, DateTimeOffset now)
    {
        if (balanceCents <= 0) return Result<RefundRequest>.Fail("refund.nothing_to_refund", "There is no money left in this wallet.");
        return Result<RefundRequest>.Ok(new RefundRequest { WalletId = walletId, SchoolId = schoolId, RequestedBy = parent, AmountCents = balanceCents, Status = RefundStatus.Requested, CreatedAt = now });
    }

    /// <summary>The school confirms the child has left. The amount is whatever is in the wallet now, which may be less than when it was asked for.</summary>
    public Result CanApprove(long currentBalanceCents)
    {
        if (Status != RefundStatus.Requested) return Result.Fail("refund.not_requested", "This request has already been handled.");
        if (currentBalanceCents <= 0) return Result.Fail("refund.nothing_to_refund", "There is no money left in this wallet.");
        return Result.Ok();
    }

    public Result Approve(Guid reviewer, long currentBalanceCents)
    {
        var can = CanApprove(currentBalanceCents);
        if (!can.IsSuccess) return can;
        AmountCents = Math.Min(AmountCents, currentBalanceCents);
        Status = RefundStatus.Approved;
        ReviewedBy = reviewer;
        Touch();
        return Result.Ok();
    }

    public Result Reject(Guid reviewer)
    {
        if (Status != RefundStatus.Requested) return Result.Fail("refund.not_requested", "This request has already been handled.");
        Status = RefundStatus.Rejected;
        ReviewedBy = reviewer;
        Touch();
        return Result.Ok();
    }

    public void MarkPaid(string partnerRef)
    {
        Status = RefundStatus.Paid;
        PartnerRef = partnerRef;
        Touch();
    }
}

public enum PayoutStatus { Paid, Failed }

/// <summary>W5-1: money paid out to the tuckshop owner from what wallet sales earned.</summary>
public sealed class ShopPayout : Entity
{
    private ShopPayout() { }

    public ShopPayout(Guid shopId, long amountCents, PayoutStatus status, string? partnerRef, string? error, DateOnly day, DateTimeOffset now)
    {
        ShopId = shopId;
        AmountCents = amountCents;
        Status = status;
        PartnerRef = partnerRef;
        Error = error;
        Day = day;
        CreatedAt = now;
    }

    public Guid ShopId { get; private set; }
    public long AmountCents { get; private set; }
    public PayoutStatus Status { get; private set; }
    public string? PartnerRef { get; private set; }
    public string? Error { get; private set; }
    public DateOnly Day { get; private set; }
}

public enum ReconciliationStatus { Matched, Mismatch, Resolved }

/// <summary>W5-3: our ledger against the partner's own total. A difference blocks payouts until someone has looked at it.</summary>
public sealed class WalletReconciliation : VersionedEntity
{
    private WalletReconciliation() { }

    public DateOnly Day { get; private set; }
    public long PartnerTotalCents { get; private set; }
    public long LedgerTotalCents { get; private set; }
    public long DifferenceCents { get; private set; }
    public ReconciliationStatus Status { get; private set; }
    public string? Note { get; private set; }
    public Guid? ResolvedBy { get; private set; }

    /// <summary>Both totals are what is held for the platform: the partner's balance and the negative of the partner float account.</summary>
    public static WalletReconciliation Run(DateOnly day, long partnerTotalCents, long ledgerTotalCents, DateTimeOffset now)
    {
        var diff = partnerTotalCents - ledgerTotalCents;
        return new WalletReconciliation
        {
            Day = day, PartnerTotalCents = partnerTotalCents, LedgerTotalCents = ledgerTotalCents, DifferenceCents = diff,
            Status = diff == 0 ? ReconciliationStatus.Matched : ReconciliationStatus.Mismatch, CreatedAt = now,
        };
    }

    /// <summary>The ledger itself is broken (a transaction that does not add up), whatever the totals say.</summary>
    public void FlagIntegrity(string note)
    {
        Status = ReconciliationStatus.Mismatch;
        Note = note;
    }

    public Result Resolve(Guid admin, string? note)
    {
        if (Status != ReconciliationStatus.Mismatch) return Result.Fail("reconciliation.not_open", "There is nothing to resolve.");
        if ((note?.Trim() ?? "").Length < 5) return Result.Fail("reconciliation.note_required", "Write what was found and what was done.");
        Status = ReconciliationStatus.Resolved;
        Note = note!.Trim();
        ResolvedBy = admin;
        Touch();
        return Result.Ok();
    }
}

/// <summary>W5-4: a parent's request to have their child's data removed. The work is done within 30 days.</summary>
public sealed class ErasureRequest : Entity
{
    public static readonly TimeSpan Deadline = TimeSpan.FromDays(30);

    private ErasureRequest() { }

    public ErasureRequest(Guid holderId, Guid requestedBy, DateTimeOffset now)
    {
        HolderId = holderId;
        RequestedBy = requestedBy;
        DueBy = now + Deadline;
        CreatedAt = now;
    }

    public Guid HolderId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset DueBy { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void Complete(DateTimeOffset now) => CompletedAt ??= now;
}

public enum TopUpStatus { Pending, Completed, Failed }

/// <summary>W2-2: a card or instant-EFT top-up through the partner's hosted checkout. The wallet is credited only when the partner confirms.</summary>
public sealed class TopUp : VersionedEntity
{
    public const long MinCents = 2_000;
    public const long MaxCents = 500_000;

    private TopUp() { }

    public Guid WalletId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public long AmountCents { get; private set; }
    public TopUpStatus Status { get; private set; }
    public string? PartnerRef { get; private set; }
    public string? CheckoutUrl { get; private set; }

    public static Result<TopUp> Start(Guid walletId, Guid parent, long amountCents, DateTimeOffset now)
    {
        if (amountCents is < MinCents or > MaxCents) return Result<TopUp>.Fail("topup.amount_invalid", "A top-up is from R20 to R5000.");
        return Result<TopUp>.Ok(new TopUp { WalletId = walletId, RequestedBy = parent, AmountCents = amountCents, Status = TopUpStatus.Pending, CreatedAt = now });
    }

    public void SetCheckout(string partnerRef, string url)
    {
        PartnerRef = partnerRef;
        CheckoutUrl = url;
        Touch();
    }

    public Result Complete(long paidCents)
    {
        if (Status == TopUpStatus.Completed) return Result.Ok();
        if (Status != TopUpStatus.Pending) return Result.Fail("topup.not_pending", "This top-up is not waiting for money.");
        if (paidCents != AmountCents)
        {
            Status = TopUpStatus.Failed;
            Touch();
            return Result.Fail("topup.amount_mismatch", "The amount paid does not match the top-up.");
        }
        Status = TopUpStatus.Completed;
        Touch();
        return Result.Ok();
    }

    public void Fail()
    {
        if (Status != TopUpStatus.Pending) return;
        Status = TopUpStatus.Failed;
        Touch();
    }
}

/// <summary>A partner callback already handled, so a repeat does nothing.</summary>
public sealed class WalletWebhookReceipt : Entity
{
    private WalletWebhookReceipt() { }

    public WalletWebhookReceipt(string partner, string eventId, DateTimeOffset now)
    {
        Partner = partner;
        EventId = eventId;
        CreatedAt = now;
    }

    public string Partner { get; private set; } = default!;
    public string EventId { get; private set; } = default!;
}
