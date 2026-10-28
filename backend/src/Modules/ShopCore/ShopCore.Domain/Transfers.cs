using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public enum TransferStatus { Sent, Received, Cancelled }

public sealed record TransferLineInput(Guid FromProductId, Guid ToProductId, string Name, int Quantity);

/// <summary>
/// G1-2: stock sent from one branch to another. Stock leaves the sending branch when it is sent and joins the receiving branch when it is received;
/// until then it is in transit. It is a pair of linked stock movements, so stock stays append-only.
/// </summary>
public sealed class StockTransfer : Entity
{
    private readonly List<StockTransferLine> _lines = [];

    private StockTransfer() { }

    public Guid FromShopId { get; private set; }
    public Guid ToShopId { get; private set; }
    public TransferStatus Status { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public string? Note { get; private set; }
    public IReadOnlyList<StockTransferLine> Lines => _lines;

    public static Result<StockTransfer> Send(Guid id, Guid fromShopId, Guid toShopId, Guid staffId, Guid deviceId, IReadOnlyList<TransferLineInput> lines, string? note, DateTimeOffset now)
    {
        if (fromShopId == toShopId) return Result<StockTransfer>.Fail("transfer.same_shop", "Choose a different branch to send to.");
        if (lines.Count == 0) return Result<StockTransfer>.Fail("transfer.empty", "Add at least one product.");
        if (lines.Any(l => l.Quantity <= 0)) return Result<StockTransfer>.Fail("transfer.quantity_invalid", "Every quantity must be one or more.");
        if (lines.GroupBy(l => l.FromProductId).Any(g => g.Count() > 1)) return Result<StockTransfer>.Fail("transfer.duplicate_product", "A product can only be on the transfer once.");
        if ((note?.Length ?? 0) > 300) return Result<StockTransfer>.Fail("transfer.note_too_long", "The note is too long.");

        var t = new StockTransfer { FromShopId = fromShopId, ToShopId = toShopId, Status = TransferStatus.Sent, SentAt = now, Note = note?.Trim() };
        t.StampFromDevice(id, now, staffId, deviceId);
        foreach (var l in lines) t._lines.Add(new StockTransferLine(id, l.FromProductId, l.ToProductId, l.Name, l.Quantity, now));
        return Result<StockTransfer>.Ok(t);
    }

    /// <summary>
    /// The receiving branch counts what arrived. Anything less than was sent is a shortfall (lost or damaged on the way) and is shown on the transfer;
    /// stock already left the sender, so no second movement is made for it.
    /// </summary>
    public Result Receive(Guid staffId, IReadOnlyDictionary<Guid, int>? received, DateTimeOffset now)
    {
        if (Status != TransferStatus.Sent) return Result.Fail("transfer.not_in_transit", Status == TransferStatus.Received ? "This transfer was already received." : "This transfer was cancelled.");
        foreach (var l in _lines)
        {
            var qty = received is not null && received.TryGetValue(l.FromProductId, out var q) ? q : l.Quantity;
            if (qty < 0 || qty > l.Quantity) return Result.Fail("transfer.received_invalid", $"{l.Name}: you cannot receive more than was sent or less than none.");
        }
        foreach (var l in _lines) l.SetReceived(received is not null && received.TryGetValue(l.FromProductId, out var q) ? q : l.Quantity);
        Status = TransferStatus.Received;
        ClosedAt = now;
        ClosedBy = staffId;
        return Result.Ok();
    }

    public Result Cancel(Guid staffId, DateTimeOffset now)
    {
        if (Status != TransferStatus.Sent) return Result.Fail("transfer.not_in_transit", Status == TransferStatus.Received ? "This transfer was already received." : "This transfer was cancelled.");
        Status = TransferStatus.Cancelled;
        ClosedAt = now;
        ClosedBy = staffId;
        return Result.Ok();
    }
}

public sealed class StockTransferLine : Entity
{
    private StockTransferLine() { }

    public StockTransferLine(Guid transferId, Guid fromProductId, Guid toProductId, string name, int quantity, DateTimeOffset now)
    {
        TransferId = transferId;
        FromProductId = fromProductId;
        ToProductId = toProductId;
        Name = name;
        Quantity = quantity;
        CreatedAt = now;
    }

    public Guid TransferId { get; private set; }
    public Guid FromProductId { get; private set; }
    public Guid ToProductId { get; private set; }
    public string Name { get; private set; } = default!;
    public int Quantity { get; private set; }
    public int? ReceivedQuantity { get; private set; }

    public int Shortfall => ReceivedQuantity is { } r ? Quantity - r : 0;

    internal void SetReceived(int quantity) => ReceivedQuantity = quantity;
}
