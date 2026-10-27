using TillKnowIT.SharedKernel;

namespace Payments.Domain;

public enum DisputeSide { Shop, Supplier }
public enum DisputeStatus { Open, Resolved }

/// <summary>What the platform admin decides: pay the supplier what was held, or give it all back to the shop.</summary>
public enum DisputeOutcome { PaySupplier, RefundShop }

/// <summary>M5-3: either side of a delivered order can say something is wrong. The payment stays held until an admin resolves it.</summary>
public sealed class Dispute : VersionedEntity
{
    private Dispute() { }

    public Guid OrderId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid RaisedByPersonId { get; private set; }
    public DisputeSide RaisedBy { get; private set; }
    public string Reason { get; private set; } = default!;
    public DisputeStatus Status { get; private set; }
    public DisputeOutcome? Outcome { get; private set; }
    public string? Note { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public static Result<Dispute> Raise(Guid orderId, Guid shopId, Guid supplierId, Guid person, DisputeSide side, string? reason, DateTimeOffset now)
    {
        var text = reason?.Trim() ?? "";
        if (text.Length < 5) return Result<Dispute>.Fail("dispute.reason_required", "Say what is wrong, in a few words.");
        if (text.Length > 1000) return Result<Dispute>.Fail("dispute.reason_too_long", "Keep the reason under 1000 characters.");
        return Result<Dispute>.Ok(new Dispute
        {
            OrderId = orderId, ShopId = shopId, SupplierId = supplierId, RaisedByPersonId = person, RaisedBy = side, Reason = text,
            Status = DisputeStatus.Open, CreatedBy = person, CreatedAt = now,
        });
    }

    /// <summary>Whether it can be resolved with this note, without changing anything.</summary>
    public Result CanResolve(string? note)
    {
        if (Status == DisputeStatus.Resolved) return Result.Fail("dispute.already_resolved", "This dispute has already been resolved.");
        if ((note?.Trim() ?? "").Length < 3) return Result.Fail("dispute.note_required", "Write a short note on why.");
        return Result.Ok();
    }

    public Result Resolve(DisputeOutcome outcome, string? note, Guid admin, DateTimeOffset now)
    {
        var can = CanResolve(note);
        if (!can.IsSuccess) return can;
        var text = note!.Trim();
        Status = DisputeStatus.Resolved;
        Outcome = outcome;
        Note = text.Length > 1000 ? text[..1000] : text;
        ResolvedBy = admin;
        ResolvedAt = now;
        Touch();
        return Result.Ok();
    }
}

/// <summary>One day's comparison of our payments with the partner's statement. Matches are to the cent.</summary>
public sealed class ReconciliationRun : Entity
{
    private ReconciliationRun() { }

    public ReconciliationRun(DateOnly day, int matched, string mismatchesJson, DateTimeOffset at)
    {
        Day = day;
        Matched = matched;
        MismatchesJson = mismatchesJson;
        CreatedAt = at;
    }

    public DateOnly Day { get; private set; }
    public int Matched { get; private set; }
    public string MismatchesJson { get; private set; } = "[]";
}
