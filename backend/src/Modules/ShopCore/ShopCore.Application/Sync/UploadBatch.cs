using Identity.Contracts;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Sync;

public sealed record UploadBatch(SyncUploadRequest Request) : ICommand<SyncUploadResponse>;

public interface IUploadStore
{
    Task<HashSet<Guid>> ExistingSaleIds(IEnumerable<Guid> ids, CancellationToken ct);
    Task<HashSet<Guid>> ExistingMovementIds(IEnumerable<Guid> ids, CancellationToken ct);
    Task<HashSet<Guid>> ExistingCancellationIds(IEnumerable<Guid> ids, CancellationToken ct);
    Task<HashSet<Guid>> CancelledSaleIds(IEnumerable<Guid> saleIds, CancellationToken ct);
    Task<HashSet<Guid>> ExistingProductIds(Guid shopId, IEnumerable<Guid> ids, CancellationToken ct);

    /// <summary>Quantity sold per product, for each of the given sales that belong to the shop.</summary>
    Task<Dictionary<Guid, Dictionary<Guid, int>>> SaleQuantities(Guid shopId, IEnumerable<Guid> saleIds, CancellationToken ct);

    void Add(Sale sale);
    void Add(StockMovement movement);
    void Add(SaleCancellation cancellation);
    Task Save(CancellationToken ct);
}

internal sealed class UploadBatchHandler(IUploadStore store, ITillAccessChecker access, TimeProvider clock)
    : ICommandHandler<UploadBatch, SyncUploadResponse>
{
    public async Task<Result<SyncUploadResponse>> Handle(UploadBatch command, CancellationToken ct)
    {
        var req = command.Request;
        if (req.Count > SyncUploadRequest.MaxRecords)
            return Result<SyncUploadResponse>.Fail("upload.too_large", $"Send at most {SyncUploadRequest.MaxRecords} records at a time.");

        var sales = req.Sales ?? [];
        var movements = req.StockMovements ?? [];
        var cancellations = req.Cancellations ?? [];
        var results = new List<UploadRecordResult>(req.Count);
        if (req.Count == 0) return Result<SyncUploadResponse>.Ok(new SyncUploadResponse(results));

        var now = clock.GetUtcNow();

        // One round of lookups for the whole batch.
        var existingSales = await store.ExistingSaleIds(sales.Select(s => s.Id), ct);
        var existingMovements = await store.ExistingMovementIds(movements.Select(m => m.Id), ct);
        var existingCancellations = await store.ExistingCancellationIds(cancellations.Select(c => c.Id), ct);
        var alreadyCancelled = await store.CancelledSaleIds(cancellations.Select(c => c.SaleId), ct);
        var productIds = sales.SelectMany(s => s.Lines ?? []).Select(l => l.ProductId)
            .Concat(movements.Select(m => m.ProductId)).Distinct().ToList();
        var knownProducts = await store.ExistingProductIds(req.ShopId, productIds, ct);
        var referencedSales = movements.Where(m => m.SaleId is not null).Select(m => m.SaleId!.Value)
            .Concat(cancellations.Select(c => c.SaleId)).Distinct().ToList();
        var soldQuantities = await store.SaleQuantities(req.ShopId, referencedSales, ct);

        var accessCache = new Dictionary<(Guid Staff, string Permission), Result>();
        async Task<Result> Allowed(Guid staffId, string permission)
        {
            if (!accessCache.TryGetValue((staffId, permission), out var r))
                accessCache[(staffId, permission)] = r = await access.Check(req.ShopId, req.DeviceId, staffId, permission, ct);
            return r;
        }

        void Add(string kind, Guid id, string status, string? code = null, string? message = null) =>
            results.Add(new UploadRecordResult(kind, id, status, code, message));
        void Reject(string kind, Guid id, Error error) => Add(kind, id, UploadStatus.Rejected, error.Code, error.Message);

        var seen = new HashSet<Guid>();

        // ---- sales ----
        foreach (var s in sales)
        {
            const string kind = "sale";
            if (existingSales.Contains(s.Id) || !seen.Add(s.Id)) { Add(kind, s.Id, UploadStatus.Duplicate); continue; }

            var allowed = await Allowed(s.StaffId, "sale.create");
            if (!allowed.IsSuccess) { Reject(kind, s.Id, allowed.Error!); continue; }

            var lines = s.Lines ?? [];
            if (lines.Any(l => !knownProducts.Contains(l.ProductId)))
            {
                Reject(kind, s.Id, new Error("product.unknown", "A product in this sale is not on the server yet. Upload products first."));
                continue;
            }

            var recorded = Sale.Record(new SaleInput(s.Id, req.ShopId, req.DeviceId, s.StaffId, s.SoldAt, s.PaymentMethodCode,
                s.TotalCents, s.VatCents, s.TenderedCents, s.ChangeCents, s.AccountId,
                lines.Select(l => new SaleLineInput(l.ProductId, l.Name, l.Quantity, l.PriceCents, l.CostCents)).ToList()), now);
            if (!recorded.IsSuccess) { Reject(kind, s.Id, recorded.Error!); continue; }

            store.Add(recorded.Value!);
            soldQuantities[s.Id] = recorded.Value!.Lines.ToDictionary(l => l.ProductId, l => l.Quantity);
            Add(kind, s.Id, UploadStatus.Accepted);
        }

        // ---- stock movements ----
        foreach (var m in movements)
        {
            const string kind = "stock_movement";
            if (existingMovements.Contains(m.Id) || !seen.Add(m.Id)) { Add(kind, m.Id, UploadStatus.Duplicate); continue; }

            if (!TryParseType(m.Type, out var type))
            {
                Reject(kind, m.Id, new Error("stock.type_invalid", $"Unknown movement type '{m.Type}'."));
                continue;
            }

            var allowed = await Allowed(m.StaffId, PermissionFor(type));
            if (!allowed.IsSuccess) { Reject(kind, m.Id, allowed.Error!); continue; }

            if (!knownProducts.Contains(m.ProductId))
            {
                Reject(kind, m.Id, new Error("product.unknown", "This product is not on the server yet. Upload products first."));
                continue;
            }

            if (type is StockMovementType.Sale or StockMovementType.Cancel && m.SaleId is { } saleId)
            {
                if (!soldQuantities.TryGetValue(saleId, out var sold))
                {
                    Reject(kind, m.Id, new Error("stock.sale_unknown", "The sale for this movement is not on the server."));
                    continue;
                }
                if (!sold.TryGetValue(m.ProductId, out var soldQty))
                {
                    Reject(kind, m.Id, new Error("stock.not_in_sale", "This product was not part of that sale."));
                    continue;
                }
                if (Math.Abs(m.Quantity) > soldQty)
                {
                    Reject(kind, m.Id, new Error("stock.exceeds_sale", "The quantity is more than was sold."));
                    continue;
                }
            }

            var recorded = StockMovement.Record(new StockMovementInput(m.Id, req.ShopId, m.ProductId, type, m.Quantity,
                m.StaffId, req.DeviceId, m.CreatedAt, m.SaleId, m.UnitCostCents, m.ReasonCode, m.SupplierName, m.InvoiceRef));
            if (!recorded.IsSuccess) { Reject(kind, m.Id, recorded.Error!); continue; }

            store.Add(recorded.Value!);
            Add(kind, m.Id, UploadStatus.Accepted);
        }

        // ---- cancellations ----
        foreach (var c in cancellations)
        {
            const string kind = "cancellation";
            if (existingCancellations.Contains(c.Id) || !seen.Add(c.Id)) { Add(kind, c.Id, UploadStatus.Duplicate); continue; }

            var allowed = await Allowed(c.StaffId, "sale.cancel");
            if (!allowed.IsSuccess) { Reject(kind, c.Id, allowed.Error!); continue; }

            if (!soldQuantities.ContainsKey(c.SaleId))
            {
                Reject(kind, c.Id, new Error("cancellation.sale_unknown", "The sale to cancel is not on the server."));
                continue;
            }
            if (alreadyCancelled.Contains(c.SaleId))
            {
                Reject(kind, c.Id, new Error("cancellation.already_cancelled", "This sale was already cancelled."));
                continue;
            }

            var recorded = SaleCancellation.Record(c.Id, c.SaleId, req.ShopId, c.StaffId, req.DeviceId, c.Reason, c.CancelledAt);
            if (!recorded.IsSuccess) { Reject(kind, c.Id, recorded.Error!); continue; }

            alreadyCancelled.Add(c.SaleId);
            store.Add(recorded.Value!);
            Add(kind, c.Id, UploadStatus.Accepted);
        }

        // Everything accepted is saved in one transaction.
        await store.Save(ct);
        return Result<SyncUploadResponse>.Ok(new SyncUploadResponse(results));
    }

    private static string PermissionFor(StockMovementType type) => type switch
    {
        StockMovementType.Receive => "stock.receive",
        StockMovementType.Sale => "sale.create",
        StockMovementType.Cancel => "sale.cancel",
        _ => "stock.count",
    };

    private static bool TryParseType(string value, out StockMovementType type)
    {
        switch (value)
        {
            case "receive": type = StockMovementType.Receive; return true;
            case "sale": type = StockMovementType.Sale; return true;
            case "cancel": type = StockMovementType.Cancel; return true;
            case "count": type = StockMovementType.Count; return true;
            case "write_off": type = StockMovementType.WriteOff; return true;
            default: type = default; return false;
        }
    }
}
