using Identity.Contracts;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Stock;

public sealed record TransferProduct(Guid Id, string Name, string? Barcode, bool IsActive);

public interface ITransferStore
{
    void Add(StockTransfer transfer);
    void Add(StockMovement movement);
    Task<StockTransfer?> Get(Guid transferId, CancellationToken ct);
    Task<List<StockTransfer>> For(Guid shopId, string? status, int take, CancellationToken ct);
    Task<List<TransferProduct>> Products(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<List<TransferProduct>> ByBarcodes(Guid shopId, IReadOnlyCollection<string> barcodes, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public sealed record SendTransfer(Guid FromShopId, SendTransferRequest Request) : ICommand<TransferResponse>;
public sealed record ReceiveTransfer(Guid ToShopId, Guid TransferId, ReceiveTransferRequest Request) : ICommand<TransferResponse>;
public sealed record CancelTransfer(Guid FromShopId, Guid TransferId, CancelTransferRequest Request) : ICommand<TransferResponse>;
public sealed record ListTransfers(Guid ShopId, Guid DeviceId, Guid StaffId, string? Status) : IQuery<List<TransferResponse>>;

internal sealed class TransferHandlers(ITransferStore store, ITillAccessChecker tills, IBranchDirectory branches, TimeProvider clock)
    : ICommandHandler<SendTransfer, TransferResponse>, ICommandHandler<ReceiveTransfer, TransferResponse>, ICommandHandler<CancelTransfer, TransferResponse>,
      IQueryHandler<ListTransfers, List<TransferResponse>>
{
    private static TransferResponse Respond(StockTransfer t) =>
        new(t.Id, t.FromShopId, t.ToShopId, t.Status.ToString().ToLowerInvariant(), t.SentAt, t.ClosedAt, t.Note,
            t.Lines.Select(l => new TransferLineResponse(l.FromProductId, l.Name, l.Quantity, l.ReceivedQuantity, l.Shortfall)).ToList());

    private static Guid MovementId(Guid transferId, string part, Guid productId) => DeterministicGuid.From($"transfer:{transferId}:{part}:{productId}");

    private void Move(StockTransfer t, Guid shopId, Guid productId, int quantity, StockMovementType type, string part, Guid staffId, Guid deviceId, string? reason, DateTimeOffset now)
    {
        var movement = StockMovement.Record(new StockMovementInput(MovementId(t.Id, part, productId), shopId, productId, type, quantity, staffId, deviceId, now, null, null, reason, null, null, t.Id));
        store.Add(movement.Value!);
    }

    public async Task<Result<TransferResponse>> Handle(SendTransfer c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await tills.Check(c.FromShopId, r.DeviceId, r.StaffId, "stock.receive", ct);
        if (!allowed.IsSuccess) return Result<TransferResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var existing = await store.Get(r.Id, ct);
        if (existing is not null) return existing.FromShopId == c.FromShopId ? Result<TransferResponse>.Ok(Respond(existing)) : Result<TransferResponse>.Fail("transfer.not_found", "Transfer not found.");

        var group = await branches.Group(c.FromShopId, ct);
        if (group.All(b => b.ShopId != r.ToShopId)) return Result<TransferResponse>.Fail("transfer.not_a_branch", "You can only send stock to your own branches.");

        var ids = (r.Lines ?? []).Select(l => l.ProductId).Distinct().ToList();
        var mine = (await store.Products(c.FromShopId, ids, ct)).ToDictionary(p => p.Id);
        if (ids.Any(i => !mine.ContainsKey(i))) return Result<TransferResponse>.Fail("product.not_found", "One of the products is not in this shop.");

        // Match each product to the receiving branch's own product: by the one named on the line, else by barcode.
        var explicitIds = (r.Lines ?? []).Where(l => l.ToProductId is not null).Select(l => l.ToProductId!.Value).ToList();
        var theirs = (await store.Products(r.ToShopId, explicitIds, ct)).ToDictionary(p => p.Id);
        var byBarcode = (await store.ByBarcodes(r.ToShopId, ids.Select(i => mine[i].Barcode).Where(b => b is not null).Select(b => b!).ToList(), ct)).ToDictionary(p => p.Barcode!);
        var lines = new List<TransferLineInput>();
        foreach (var l in r.Lines ?? [])
        {
            var p = mine[l.ProductId];
            Guid? to = l.ToProductId is { } named ? (theirs.ContainsKey(named) ? named : null) : p.Barcode is not null && byBarcode.TryGetValue(p.Barcode, out var match) ? match.Id : null;
            if (to is null) return Result<TransferResponse>.Fail("transfer.product_unmatched", $"{p.Name} is not sold at the other branch. Add it there first, or choose the matching product.");
            lines.Add(new(l.ProductId, to.Value, p.Name, l.Quantity));
        }

        var now = clock.GetUtcNow();
        var transfer = StockTransfer.Send(r.Id, c.FromShopId, r.ToShopId, r.StaffId, r.DeviceId, lines, r.Note, now);
        if (!transfer.IsSuccess) return Result<TransferResponse>.Fail(transfer.Error!.Code, transfer.Error.Message);
        store.Add(transfer.Value!);
        foreach (var l in transfer.Value!.Lines) Move(transfer.Value, c.FromShopId, l.FromProductId, -l.Quantity, StockMovementType.TransferOut, "out", r.StaffId, r.DeviceId, null, now);
        await store.Save(ct);
        return Result<TransferResponse>.Ok(Respond(transfer.Value));
    }

    public async Task<Result<TransferResponse>> Handle(ReceiveTransfer c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await tills.Check(c.ToShopId, r.DeviceId, r.StaffId, "stock.receive", ct);
        if (!allowed.IsSuccess) return Result<TransferResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var transfer = await store.Get(c.TransferId, ct);
        if (transfer is null || transfer.ToShopId != c.ToShopId) return Result<TransferResponse>.Fail("transfer.not_found", "Transfer not found.");

        var now = clock.GetUtcNow();
        var done = transfer.Receive(r.StaffId, r.Lines?.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity)), now);
        if (!done.IsSuccess) return Result<TransferResponse>.Fail(done.Error!.Code, done.Error.Message);
        foreach (var l in transfer.Lines.Where(l => l.ReceivedQuantity > 0))
            Move(transfer, c.ToShopId, l.ToProductId, l.ReceivedQuantity!.Value, StockMovementType.TransferIn, "in", r.StaffId, r.DeviceId, null, now);
        await store.Save(ct);
        return Result<TransferResponse>.Ok(Respond(transfer));
    }

    /// <summary>The sending branch can take a transfer back while it is still in transit: the stock returns to it.</summary>
    public async Task<Result<TransferResponse>> Handle(CancelTransfer c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await tills.Check(c.FromShopId, r.DeviceId, r.StaffId, "stock.receive", ct);
        if (!allowed.IsSuccess) return Result<TransferResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var transfer = await store.Get(c.TransferId, ct);
        if (transfer is null || transfer.FromShopId != c.FromShopId) return Result<TransferResponse>.Fail("transfer.not_found", "Transfer not found.");

        var now = clock.GetUtcNow();
        var done = transfer.Cancel(r.StaffId, now);
        if (!done.IsSuccess) return Result<TransferResponse>.Fail(done.Error!.Code, done.Error.Message);
        foreach (var l in transfer.Lines) Move(transfer, c.FromShopId, l.FromProductId, l.Quantity, StockMovementType.TransferIn, "back", r.StaffId, r.DeviceId, "transfer_cancelled", now);
        await store.Save(ct);
        return Result<TransferResponse>.Ok(Respond(transfer));
    }

    /// <summary>Transfers sent from or to the shop. Status "sent" is what is in transit.</summary>
    public async Task<Result<List<TransferResponse>>> Handle(ListTransfers q, CancellationToken ct)
    {
        var allowed = await tills.Check(q.ShopId, q.DeviceId, q.StaffId, "stock.receive", ct);
        if (!allowed.IsSuccess) return Result<List<TransferResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (q.Status is not null && q.Status is not ("sent" or "received" or "cancelled")) return Result<List<TransferResponse>>.Fail("transfer.status_invalid", "Status must be sent, received or cancelled.");
        return Result<List<TransferResponse>>.Ok((await store.For(q.ShopId, q.Status, 200, ct)).Select(Respond).ToList());
    }
}
