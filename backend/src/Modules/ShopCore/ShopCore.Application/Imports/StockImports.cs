using System.Text.Json;
using Identity.Contracts;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.Importing;
using TillKnowIT.SharedKernel;
using TillKnowIT.Storage;

namespace ShopCore.Application.Imports;

public interface IImportStore
{
    Task Add(InvoiceImport import, CancellationToken ct);

    /// <summary>Whether this shop already has an import for the file (or marketplace order) with this address.</summary>
    Task<bool> ExistsByFile(Guid shopId, string fileUrl, CancellationToken ct);
    Task<InvoiceImport?> Get(Guid shopId, Guid importId, CancellationToken ct);

    /// <summary>The shop's active products, as much as matching needs.</summary>
    Task<List<ProductRef>> Products(Guid shopId, CancellationToken ct);

    Task<ImportMapping?> GetMapping(Guid shopId, string supplierKey, CancellationToken ct);
    Task Add(ImportMapping mapping, CancellationToken ct);
    Task<List<InvoiceImport>> QueuedInvoices(int take, CancellationToken ct);
    Task Add(StockMovement movement, CancellationToken ct);
    Task Save(CancellationToken ct);
}

/// <summary>What the invoice reader found. A real reader (OCR or a language model) is a port: none is connected yet.</summary>
public sealed record InvoiceReading(bool Success, List<RawImportLine> Lines, string? SupplierName = null, string? InvoiceRef = null, string? Error = null);

public interface IInvoiceReader
{
    Task<InvoiceReading> Read(StoredFile file, CancellationToken ct);
}

/// <summary>What is kept with an import: the plan the owner reviews.</summary>
public sealed record StoredPlan(List<string>? Headers, Dictionary<string, string>? Mapping, List<ImportLine> Lines);

internal static class PlanJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write(StoredPlan plan) => JsonSerializer.Serialize(plan, Options);
    public static StoredPlan? Read(string? json) => json is null ? null : JsonSerializer.Deserialize<StoredPlan>(json, Options);
}

/// <summary>Turns a sheet into raw lines using the column mapping, so the same planner serves sheets and invoices.</summary>
internal static class SheetLines
{
    public static List<RawImportLine> From(Sheet sheet, ColumnMapping mapping) =>
        sheet.Rows.Select((row, i) =>
        {
            string? Cell(string field) => ColumnMapper.Cell(sheet, row, mapping, field);
            return new RawImportLine(
                Row: i + 2,   // the row number in the file, with the header as row 1
                Text: Cell(ColumnMapper.Name) ?? "",
                Barcode: Cell(ColumnMapper.Barcode),
                Quantity: ColumnMapper.ParseWholeNumber(Cell(ColumnMapper.Quantity)),
                UnitsPerCase: ColumnMapper.ParseWholeNumber(Cell(ColumnMapper.UnitsPerCase)),
                CaseCostCents: ColumnMapper.ParseMoneyCents(Cell(ColumnMapper.CaseCost)),
                UnitCostCents: ColumnMapper.ParseMoneyCents(Cell(ColumnMapper.UnitCost)));
        }).ToList();
}

// ---------------------------------------------------------------- start

public sealed record StartStockImport(
    Guid ShopId, Guid DeviceId, Guid StaffId, string FileName, string ContentType, byte[] Content, string? SupplierName)
    : ICommand<StartStockImportResponse>;

internal sealed class StartStockImportHandler(IImportStore store, IFileStore files, ITillAccessChecker access, TimeProvider clock)
    : ICommandHandler<StartStockImport, StartStockImportResponse>
{
    private static readonly string[] SheetExtensions = [".csv", ".txt", ".xlsx"];

    public async Task<Result<StartStockImportResponse>> Handle(StartStockImport c, CancellationToken ct)
    {
        var allowed = await access.Check(c.ShopId, c.DeviceId, c.StaffId, "stock.receive", ct);
        if (!allowed.IsSuccess) return Result<StartStockImportResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var isSheet = SheetExtensions.Contains(Path.GetExtension(c.FileName).ToLowerInvariant());
        Sheet? sheet = null;
        if (isSheet)
        {
            var read = SheetReader.Read(c.FileName, c.Content);
            if (!read.IsSuccess) return Result<StartStockImportResponse>.Fail(read.Error!.Code, read.Error.Message);
            sheet = read.Value;
        }
        else
        {
            var valid = FileRules.Validate(c.FileName, c.ContentType, c.Content);
            if (!valid.IsSuccess) return Result<StartStockImportResponse>.Fail(valid.Error!.Code, valid.Error.Message);
        }

        var now = clock.GetUtcNow();
        var url = await files.Save("imports", c.FileName, c.ContentType, c.Content, ct);
        var import = new InvoiceImport(c.ShopId, isSheet ? ImportKind.Sheet : ImportKind.Invoice, url, c.FileName, c.SupplierName, c.StaffId, c.DeviceId, now);

        if (sheet is not null)
        {
            // A saved choice of columns for this supplier saves the owner matching them again.
            ColumnMapping? saved = null;
            if (import.SupplierName is not null && await store.GetMapping(c.ShopId, ImportMapping.KeyFor(import.SupplierName), ct) is { } m)
                saved = JsonSerializer.Deserialize<ColumnMapping>(m.MappingJson);
            var mapping = ColumnMapper.Reconcile(saved, sheet.Headers);
            var plan = ImportPlanner.Plan(SheetLines.From(sheet, mapping), await store.Products(c.ShopId, ct));
            import.Complete(PlanJson.Write(new StoredPlan([.. sheet.Headers], mapping.FieldToHeader, plan)));
        }

        await store.Add(import, ct);
        await store.Save(ct);
        return Result<StartStockImportResponse>.Ok(new StartStockImportResponse(import.Id, import.Status.ToString().ToLowerInvariant()));
    }
}

// ---------------------------------------------------------------- review

public sealed record GetStockImport(Guid ShopId, Guid ImportId) : IQuery<StockImportResponse>;

internal sealed class GetStockImportHandler(IImportStore store) : IQueryHandler<GetStockImport, StockImportResponse>
{
    public async Task<Result<StockImportResponse>> Handle(GetStockImport q, CancellationToken ct)
    {
        var import = await store.Get(q.ShopId, q.ImportId, ct);
        if (import is null) return Result<StockImportResponse>.Fail("import.not_found", "Import not found.");

        var plan = PlanJson.Read(import.ResultJson);
        return Result<StockImportResponse>.Ok(new StockImportResponse(
            import.Id, import.Status.ToString().ToLowerInvariant(), import.Kind.ToString().ToLowerInvariant(), import.FileName, import.SupplierName,
            plan?.Headers, plan?.Mapping,
            (plan?.Lines ?? []).Select(l => new StockImportLineResponse(l.Row, l.Text, l.Barcode, l.ProductId, l.MatchedBy, l.Candidates, l.Quantity, l.UnitsPerCase, l.UnitCostCents, l.Issues)).ToList(),
            import.Error));
    }
}

/// <summary>The owner corrects which column is which; the review table is worked out again from the same file.</summary>
public sealed record RemapStockImport(Guid ShopId, Guid ImportId, RemapStockImportRequest Request) : ICommand<Unit>;

internal sealed class RemapStockImportHandler(IImportStore store, IFileStore files, ITillAccessChecker access, TimeProvider clock)
    : ICommandHandler<RemapStockImport, Unit>
{
    public async Task<Result<Unit>> Handle(RemapStockImport c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await access.Check(c.ShopId, r.DeviceId, r.StaffId, "stock.receive", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var import = await store.Get(c.ShopId, c.ImportId, ct);
        if (import is null) return Result<Unit>.Fail("import.not_found", "Import not found.");
        if (import.Kind != ImportKind.Sheet || import.Status != ImportStatus.Ready)
            return Result<Unit>.Fail("import.not_remappable", "Only a sheet that has not been received yet can be re-mapped.");
        if (r.Mapping.Keys.Any(k => !ColumnMapper.Fields.Contains(k)))
            return Result<Unit>.Fail("import.mapping_invalid", $"Fields are: {string.Join(", ", ColumnMapper.Fields)}.");

        var file = await files.Open(import.FileUrl, ct);
        if (file is null) return Result<Unit>.Fail("import.file_unavailable", "The file could not be read. Upload it again.");
        var read = SheetReader.Read(import.FileName, file.Content);
        if (!read.IsSuccess) return Result<Unit>.Fail(read.Error!.Code, read.Error.Message);

        var headers = read.Value!.Headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (r.Mapping.Values.Any(h => !headers.Contains(h)))
            return Result<Unit>.Fail("import.mapping_invalid", "A chosen column is not in the file.");

        var mapping = new ColumnMapping(r.Mapping);
        var plan = ImportPlanner.Plan(SheetLines.From(read.Value, mapping), await store.Products(c.ShopId, ct));
        import.Complete(PlanJson.Write(new StoredPlan([.. read.Value.Headers], mapping.FieldToHeader, plan)));

        if (import.SupplierName is not null)
        {
            var json = JsonSerializer.Serialize(mapping);
            var existing = await store.GetMapping(c.ShopId, ImportMapping.KeyFor(import.SupplierName), ct);
            if (existing is null) await store.Add(new ImportMapping(c.ShopId, import.SupplierName, json, clock.GetUtcNow()), ct);
            else existing.Update(json);
        }
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}

// ---------------------------------------------------------------- confirm

/// <summary>E4-2 and E4-3: the owner has reviewed the lines. Only now does anything reach stock, once, as receive movements.</summary>
public sealed record ConfirmStockImport(Guid ShopId, Guid ImportId, ConfirmStockImportRequest Request) : ICommand<int>;

internal sealed class ConfirmStockImportHandler(IImportStore store, ITillAccessChecker access, TimeProvider clock) : ICommandHandler<ConfirmStockImport, int>
{
    public async Task<Result<int>> Handle(ConfirmStockImport c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await access.Check(c.ShopId, r.DeviceId, r.StaffId, "stock.receive", ct);
        if (!allowed.IsSuccess) return Result<int>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var import = await store.Get(c.ShopId, c.ImportId, ct);
        if (import is null) return Result<int>.Fail("import.not_found", "Import not found.");

        var known = (await store.Products(c.ShopId, ct)).Select(p => p.Id).ToHashSet();
        var now = clock.GetUtcNow();
        var movements = ImportConfirmation.ToMovements(r.Lines.Select(l => new ConfirmedLine(l.ProductId, l.Quantity, l.UnitCostCents)), known,
            c.ShopId, r.StaffId, r.DeviceId, r.SupplierName ?? import.SupplierName, r.InvoiceRef, now);
        if (!movements.IsSuccess) return Result<int>.Fail(movements.Error!.Code, movements.Error.Message);

        var confirmed = import.Confirm();
        if (!confirmed.IsSuccess) return Result<int>.Fail(confirmed.Error!.Code, confirmed.Error.Message);

        foreach (var input in movements.Value!)
        {
            var movement = StockMovement.Record(input);
            if (!movement.IsSuccess) return Result<int>.Fail(movement.Error!.Code, movement.Error.Message);
            await store.Add(movement.Value!, ct);
        }
        await store.Save(ct);
        return Result<int>.Ok(movements.Value!.Count);
    }
}

// ---------------------------------------------------------------- reading photos and PDFs (worker)

/// <summary>Reads queued invoice photos and PDFs. Photos taken offline wait here until the phone is online and the file has arrived.</summary>
public sealed record ReadQueuedInvoices(int Take = 5) : ICommand<int>;

internal sealed class ReadQueuedInvoicesHandler(IImportStore store, IFileStore files, IEnumerable<IInvoiceReader> readers)
    : ICommandHandler<ReadQueuedInvoices, int>
{
    public async Task<Result<int>> Handle(ReadQueuedInvoices c, CancellationToken ct)
    {
        var reader = readers.FirstOrDefault();
        var done = 0;
        foreach (var import in await store.QueuedInvoices(c.Take, ct))
        {
            if (reader is null)
            {
                import.Fail("No invoice reader is connected yet. Enter the delivery by scanning, or upload an Excel or CSV file.");
                done++;
                continue;
            }

            import.StartReading();
            var file = await files.Open(import.FileUrl, ct);
            if (file is null) { import.Fail("The file could not be read. Take the photo again."); done++; continue; }

            InvoiceReading reading;
            try { reading = await reader.Read(file, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { reading = new InvoiceReading(false, [], Error: ex.Message); }

            if (!reading.Success)
            {
                import.Fail(reading.Error ?? "The invoice could not be read.");
            }
            else
            {
                var plan = ImportPlanner.Plan(reading.Lines, await store.Products(import.ShopId, ct));
                import.Complete(PlanJson.Write(new StoredPlan(null, null, plan)));
            }
            done++;
        }
        await store.Save(ct);
        return Result<int>.Ok(done);
    }
}
