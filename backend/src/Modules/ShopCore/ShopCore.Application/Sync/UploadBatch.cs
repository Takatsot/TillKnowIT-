using Catalogue.Contracts;
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

    /// <summary>The products with these ids, whichever shop owns them, so a clash with another shop is caught.</summary>
    Task<Dictionary<Guid, Product>> GetProducts(IEnumerable<Guid> ids, CancellationToken ct);

    /// <summary>Which product of the shop uses each barcode.</summary>
    Task<Dictionary<string, Guid>> BarcodeOwners(Guid shopId, IEnumerable<string> barcodes, CancellationToken ct);

    Task<Dictionary<Guid, CreditAccount>> GetCreditAccounts(IEnumerable<Guid> ids, CancellationToken ct);
    Task<HashSet<Guid>> ExistingCreditEntryIds(IEnumerable<Guid> ids, CancellationToken ct);

    /// <summary>Which (sale, entry type) pairs already have a credit entry, so a sale is never charged or refunded twice.</summary>
    Task<HashSet<(Guid SaleId, CreditEntryType Type)>> CreditEntryKeys(IEnumerable<Guid> saleIds, CancellationToken ct);

    /// <summary>Payment method, credit account and total of each of these sales that belong to the shop.</summary>
    Task<Dictionary<Guid, SaleSummary>> SaleSummaries(Guid shopId, IEnumerable<Guid> saleIds, CancellationToken ct);

    Task<HashSet<Guid>> ExistingCashUpIds(IEnumerable<Guid> ids, CancellationToken ct);
    Task<bool> CashUpExists(Guid shopId, Guid deviceId, DateOnly day, CancellationToken ct);

    /// <summary>Cash sales made on this till in the window, with whether each has been cancelled.</summary>
    Task<List<CashSaleRow>> CashSales(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Cash paid on credit accounts at this till in the window, as a positive total.</summary>
    Task<long> CreditCashPayments(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    void Add(CreditAccount account);
    void Add(CreditEntry entry);
    void Add(CashUp cashUp);
    void Add(Product product);
    void Add(Sale sale);
    void Add(StockMovement movement);
    void Add(SaleCancellation cancellation);
    Task Save(CancellationToken ct);
}

public sealed record SaleSummary(string PaymentMethodCode, Guid? AccountId, long TotalCents);

public sealed record CashSaleRow(Guid SaleId, long TotalCents, bool Cancelled);

internal sealed class UploadBatchHandler(IUploadStore store, ITillAccessChecker access, ILookupChecker lookups, TimeProvider clock)
    : ICommandHandler<UploadBatch, SyncUploadResponse>
{
    public async Task<Result<SyncUploadResponse>> Handle(UploadBatch command, CancellationToken ct)
    {
        var req = command.Request;
        if (req.Count > SyncUploadRequest.MaxRecords)
            return Result<SyncUploadResponse>.Fail("upload.too_large", $"Send at most {SyncUploadRequest.MaxRecords} records at a time.");

        var products = req.Products ?? [];
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
        var editedProducts = await store.GetProducts(products.Select(p => p.Id), ct);
        var barcodeOwners = await store.BarcodeOwners(req.ShopId,
            products.Where(p => !string.IsNullOrWhiteSpace(p.Barcode)).Select(p => p.Barcode!.Trim()), ct);
        var activeCategories = new Dictionary<string, bool>();
        var activeReasons = new Dictionary<string, bool>();

        var creditAccountUploads = req.CreditAccounts ?? [];
        var creditEntryUploads = req.CreditEntries ?? [];
        var cashUpUploads = req.CashUps ?? [];
        var accountIds = creditAccountUploads.Select(a => a.Id)
            .Concat(creditEntryUploads.Select(e => e.AccountId))
            .Concat(sales.Where(s => s.AccountId is not null).Select(s => s.AccountId!.Value)).Distinct().ToList();
        var creditAccounts = await store.GetCreditAccounts(accountIds, ct);
        var existingCreditEntries = await store.ExistingCreditEntryIds(creditEntryUploads.Select(e => e.Id), ct);
        var existingCashUps = await store.ExistingCashUpIds(cashUpUploads.Select(c => c.Id), ct);
        var creditSaleIds = creditEntryUploads.Where(e => e.SaleId is not null).Select(e => e.SaleId!.Value).Distinct().ToList();
        foreach (var cancelledId in await store.CancelledSaleIds(creditSaleIds, ct)) alreadyCancelled.Add(cancelledId);
        var saleSummaries = await store.SaleSummaries(req.ShopId, creditSaleIds, ct);
        var creditKeys = await store.CreditEntryKeys(creditSaleIds, ct);
        var batchCashSales = new List<Sale>();
        var batchCashPayments = new List<CreditEntry>();
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

        // ---- products (first, so sales and movements in the same batch can use them) ----
        foreach (var p in products)
        {
            const string kind = "product";

            var allowed = await Allowed(p.StaffId, "product.edit");
            if (!allowed.IsSuccess) { Reject(kind, p.Id, allowed.Error!); continue; }

            var input = new ProductInput(p.Id, req.ShopId, p.StaffId, req.DeviceId, p.CatalogueProductId, p.Name, p.Barcode,
                p.CategoryCode, p.CostCents, p.PriceCents, p.LowStockLevel, p.ExpiryDate, p.IsActive, p.UpdatedAt);
            var checkedInput = Product.Check(input, now);
            if (!checkedInput.IsSuccess) { Reject(kind, p.Id, checkedInput.Error!); continue; }
            input = checkedInput.Value!;

            if (!activeCategories.TryGetValue(input.CategoryCode, out var categoryOk))
                activeCategories[input.CategoryCode] = categoryOk = await lookups.IsActive("categories", input.CategoryCode, ct);
            if (!categoryOk) { Reject(kind, p.Id, new Error("product.category_invalid", "Unknown category.")); continue; }

            if (input.Barcode is not null && barcodeOwners.TryGetValue(input.Barcode, out var owner) && owner != p.Id)
            {
                Reject(kind, p.Id, new Error("product.duplicate_barcode", "Another product in this shop already uses this barcode."));
                continue;
            }

            string? oldBarcode = null;
            if (editedProducts.TryGetValue(p.Id, out var existing))
            {
                if (existing.ShopId != req.ShopId)
                {
                    Reject(kind, p.Id, new Error("product.id_taken", "This product id belongs to another shop."));
                    continue;
                }
                oldBarcode = existing.Barcode;
                var updated = existing.Update(input);
                if (!updated.IsSuccess)
                {
                    // Not an error: the server already has the same or a newer change, so the till can drop this one.
                    Add(kind, p.Id, UploadStatus.Duplicate, updated.Error!.Code, updated.Error.Message);
                    continue;
                }
            }
            else
            {
                var created = Product.Record(input);
                store.Add(created);
                editedProducts[p.Id] = created;
            }

            if (oldBarcode is not null && oldBarcode != input.Barcode) barcodeOwners.Remove(oldBarcode);
            if (input.Barcode is not null) barcodeOwners[input.Barcode] = p.Id;
            knownProducts.Add(p.Id);
            Add(kind, p.Id, UploadStatus.Accepted);
        }

        // ---- credit customers (before sales, so a credit sale can use a customer created just before it) ----
        foreach (var a in creditAccountUploads)
        {
            const string kind = "credit_account";

            var allowed = await Allowed(a.StaffId, "credit.manage");
            if (!allowed.IsSuccess) { Reject(kind, a.Id, allowed.Error!); continue; }

            var input = new CreditAccountInput(a.Id, req.ShopId, a.StaffId, req.DeviceId, a.Name, a.Phone, a.CreditLimitCents, a.UpdatedAt);
            var checkedAccount = CreditAccount.Check(input, now);
            if (!checkedAccount.IsSuccess) { Reject(kind, a.Id, checkedAccount.Error!); continue; }
            input = checkedAccount.Value!;

            if (creditAccounts.TryGetValue(a.Id, out var existingAccount))
            {
                if (existingAccount.ShopId != req.ShopId)
                {
                    Reject(kind, a.Id, new Error("account.id_taken", "This customer id belongs to another shop."));
                    continue;
                }
                var updated = existingAccount.Update(input);
                if (!updated.IsSuccess)
                {
                    Add(kind, a.Id, UploadStatus.Duplicate, updated.Error!.Code, updated.Error.Message);
                    continue;
                }
            }
            else
            {
                var created = CreditAccount.Record(input);
                store.Add(created);
                creditAccounts[a.Id] = created;
            }
            Add(kind, a.Id, UploadStatus.Accepted);
        }

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

            if (s.AccountId is { } saleAccountId
                && !(creditAccounts.TryGetValue(saleAccountId, out var saleAccount) && saleAccount.ShopId == req.ShopId))
            {
                Reject(kind, s.Id, new Error("account.unknown", "The credit customer is not on the server yet."));
                continue;
            }

            // A sale of an expired item needs a manager's override, and the manager's role must allow it.
            var overrideDenied = false;
            foreach (var overrider in lines.Where(l => l.ExpiryOverrideBy is not null).Select(l => l.ExpiryOverrideBy!.Value).Distinct())
            {
                var may = await Allowed(overrider, "sale.override_expiry");
                if (may.IsSuccess) continue;
                Reject(kind, s.Id, new Error("sale.override_not_allowed", "The person who approved selling an expired item may not do that."));
                overrideDenied = true;
                break;
            }
            if (overrideDenied) continue;

            var recorded = Sale.Record(new SaleInput(s.Id, req.ShopId, req.DeviceId, s.StaffId, s.SoldAt, s.PaymentMethodCode,
                s.TotalCents, s.VatCents, s.TenderedCents, s.ChangeCents, s.AccountId,
                lines.Select(l => new SaleLineInput(l.ProductId, l.Name, l.Quantity, l.PriceCents, l.CostCents, l.ExpiryOverrideBy)).ToList()), now);
            if (!recorded.IsSuccess) { Reject(kind, s.Id, recorded.Error!); continue; }

            store.Add(recorded.Value!);
            soldQuantities[s.Id] = recorded.Value!.Lines.ToDictionary(l => l.ProductId, l => l.Quantity);
            saleSummaries[s.Id] = new SaleSummary(recorded.Value.PaymentMethodCode, recorded.Value.AccountId, recorded.Value.TotalCents);
            if (recorded.Value.PaymentMethodCode == PaymentMethods.Cash) batchCashSales.Add(recorded.Value);
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

            // Counts and write-offs say why, and the reason must be one the shop can pick (counted, expired, damaged, missing, own use).
            if (type is StockMovementType.Count or StockMovementType.WriteOff && !string.IsNullOrWhiteSpace(m.ReasonCode))
            {
                var reason = m.ReasonCode.Trim();
                if (!activeReasons.TryGetValue(reason, out var reasonOk))
                    activeReasons[reason] = reasonOk = await lookups.IsActive("adjustment_reasons", reason, ct);
                if (!reasonOk)
                {
                    Reject(kind, m.Id, new Error("stock.reason_invalid", "Unknown reason."));
                    continue;
                }
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

        // ---- credit entries ----
        foreach (var e in creditEntryUploads)
        {
            const string kind = "credit_entry";
            if (existingCreditEntries.Contains(e.Id) || !seen.Add(e.Id)) { Add(kind, e.Id, UploadStatus.Duplicate); continue; }

            if (!TryParseCreditType(e.Type, out var type))
            {
                Reject(kind, e.Id, new Error("credit.type_invalid", $"Unknown credit entry type '{e.Type}'."));
                continue;
            }

            var allowed = await Allowed(e.StaffId, type switch
            {
                CreditEntryType.Sale => "sale.create",
                CreditEntryType.Cancel => "sale.cancel",
                _ => "credit.payment",
            });
            if (!allowed.IsSuccess) { Reject(kind, e.Id, allowed.Error!); continue; }

            if (!(creditAccounts.TryGetValue(e.AccountId, out var account) && account.ShopId == req.ShopId))
            {
                Reject(kind, e.Id, new Error("account.unknown", "The credit customer is not on the server."));
                continue;
            }

            if (type is CreditEntryType.Sale or CreditEntryType.Cancel && e.SaleId is { } creditSaleId)
            {
                if (!saleSummaries.TryGetValue(creditSaleId, out var summary) || summary.PaymentMethodCode != PaymentMethods.Credit)
                {
                    Reject(kind, e.Id, new Error("credit.sale_unknown", "That is not a credit sale on the server."));
                    continue;
                }
                if (summary.AccountId != e.AccountId)
                {
                    Reject(kind, e.Id, new Error("credit.account_mismatch", "The sale was made to a different customer."));
                    continue;
                }
                var expectedAmount = type == CreditEntryType.Sale ? summary.TotalCents : -summary.TotalCents;
                if (e.AmountCents != expectedAmount)
                {
                    Reject(kind, e.Id, new Error("credit.amount_mismatch", "The amount does not match the sale."));
                    continue;
                }
                if (type == CreditEntryType.Cancel && !alreadyCancelled.Contains(creditSaleId))
                {
                    Reject(kind, e.Id, new Error("credit.sale_not_cancelled", "Cancel the sale first."));
                    continue;
                }
                if (!creditKeys.Add((creditSaleId, type)))
                {
                    Reject(kind, e.Id, new Error("credit.already_recorded", "This sale already has that credit entry."));
                    continue;
                }
            }

            var recorded = CreditEntry.Record(new CreditEntryInput(e.Id, e.AccountId, type, e.AmountCents, e.SaleId,
                e.PaymentMethodCode, e.StaffId, req.DeviceId, e.CreatedAt));
            if (!recorded.IsSuccess) { Reject(kind, e.Id, recorded.Error!); continue; }

            store.Add(recorded.Value!);
            if (type == CreditEntryType.Payment && e.PaymentMethodCode == PaymentMethods.Cash) batchCashPayments.Add(recorded.Value!);
            Add(kind, e.Id, UploadStatus.Accepted);
        }

        // ---- cash-ups (last, so the day's sales and payments from this batch are counted) ----
        var cashedUpToday = new HashSet<(Guid Device, DateOnly Day)>();
        foreach (var c in cashUpUploads)
        {
            const string kind = "cash_up";
            if (existingCashUps.Contains(c.Id) || !seen.Add(c.Id)) { Add(kind, c.Id, UploadStatus.Duplicate); continue; }

            var allowed = await Allowed(c.StaffId, "cashup.do");
            if (!allowed.IsSuccess) { Reject(kind, c.Id, allowed.Error!); continue; }

            var input = new CashUpInput(c.Id, req.ShopId, req.DeviceId, c.StaffId, c.Day, c.FloatCents, c.CountedCents, c.Note, c.CashedUpAt);
            var checkedCashUp = CashUp.Check(input, now);
            if (!checkedCashUp.IsSuccess) { Reject(kind, c.Id, checkedCashUp.Error!); continue; }

            if (!cashedUpToday.Add((req.DeviceId, c.Day)) || await store.CashUpExists(req.ShopId, req.DeviceId, c.Day, ct))
            {
                Reject(kind, c.Id, new Error("cashup.already_done", "This till has already been cashed up for that day."));
                continue;
            }

            // The server works out expected cash itself: sales and credit payments in cash at this till, from the start
            // of the business day to the moment of the cash-up, leaving out cancelled sales.
            var from = BusinessDay.Start(c.Day);
            var to = c.CashedUpAt;
            var storedSales = await store.CashSales(req.DeviceId, from, to, ct);
            var cashSales = storedSales.Where(s => !s.Cancelled && !alreadyCancelled.Contains(s.SaleId)).Sum(s => s.TotalCents)
                + batchCashSales.Where(s => s.SoldAt >= from && s.SoldAt <= to && !alreadyCancelled.Contains(s.Id)).Sum(s => s.TotalCents);
            var creditCash = await store.CreditCashPayments(req.DeviceId, from, to, ct)
                + batchCashPayments.Where(p => p.CreatedAt >= from && p.CreatedAt <= to).Sum(p => -p.AmountCents);
            var expected = CashUpCalculator.Expected(c.FloatCents, cashSales, creditCash);

            store.Add(CashUp.Record(input, expected));
            if (expected == c.ExpectedCents) Add(kind, c.Id, UploadStatus.Accepted);
            else
                Add(kind, c.Id, UploadStatus.Accepted, "cashup.expected_adjusted",
                    $"The till expected {c.ExpectedCents} cents; the synced records give {expected} cents. The synced figure was kept.");
        }

        // Everything accepted is saved in one transaction.
        await store.Save(ct);
        return Result<SyncUploadResponse>.Ok(new SyncUploadResponse(results));
    }

    private static bool TryParseCreditType(string value, out CreditEntryType type)
    {
        switch (value)
        {
            case "sale": type = CreditEntryType.Sale; return true;
            case "payment": type = CreditEntryType.Payment; return true;
            case "cancel": type = CreditEntryType.Cancel; return true;
            default: type = default; return false;
        }
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
