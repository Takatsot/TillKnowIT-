using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopCore.Application.Imports;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.Dispatching;
using TillKnowIT.Importing;
using TillKnowIT.Storage;

namespace ShopCore.Infrastructure;

internal sealed class ImportStore(ShopCoreDbContext db) : IImportStore
{
    public async Task Add(InvoiceImport import, CancellationToken ct) => await db.Set<InvoiceImport>().AddAsync(import, ct);

    public Task<InvoiceImport?> Get(Guid shopId, Guid importId, CancellationToken ct) =>
        db.Set<InvoiceImport>().FirstOrDefaultAsync(i => i.Id == importId && i.ShopId == shopId, ct);

    public async Task<List<ProductRef>> Products(Guid shopId, CancellationToken ct)
    {
        var rows = await db.Products.AsNoTracking().Where(p => p.ShopId == shopId && p.IsActive)
            .Select(p => new { p.Id, p.Name, p.Barcode, p.CostCents }).ToListAsync(ct);
        return rows.Select(p => new ProductRef(p.Id, p.Name, p.Barcode, p.CostCents)).ToList();
    }

    public Task<ImportMapping?> GetMapping(Guid shopId, string supplierKey, CancellationToken ct) =>
        db.Set<ImportMapping>().FirstOrDefaultAsync(m => m.ShopId == shopId && m.SupplierKey == supplierKey, ct);

    public async Task Add(ImportMapping mapping, CancellationToken ct) => await db.Set<ImportMapping>().AddAsync(mapping, ct);

    public Task<List<InvoiceImport>> QueuedInvoices(int take, CancellationToken ct) =>
        db.Set<InvoiceImport>().Where(i => i.Kind == ImportKind.Invoice && i.Status == ImportStatus.Queued).OrderBy(i => i.CreatedAt).Take(take).ToListAsync(ct);

    public async Task Add(StockMovement movement, CancellationToken ct) => await db.StockMovements.AddAsync(movement, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal static class ImportEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        // Form fields: deviceId, staffId, supplier (optional), file. Excel (.xlsx), CSV, or a JPEG, PNG or PDF of the invoice.
        api.MapPost("/shops/{shopId:guid}/stock/imports", async (Guid shopId, HttpRequest request, IDispatcher d, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Problem("import.form_required", "Send the file as a form upload.", 415);
            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null) return Problem("file.missing", "Attach the file as 'file'.", 422);
            if (file.Length > FileRules.MaxBytes) return Problem("file.too_large", "The file is larger than 10 MB.", 413);
            if (!Guid.TryParse(form["deviceId"], out var deviceId) || !Guid.TryParse(form["staffId"], out var staffId))
                return Problem("import.caller_required", "deviceId and staffId are required.", 422);

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var result = await d.Send(new StartStockImport(shopId, deviceId, staffId, Path.GetFileName(file.FileName), file.ContentType, buffer.ToArray(), form["supplier"]), ct);
            return result.IsSuccess
                ? Results.Accepted($"/api/v1/shops/{shopId}/stock/imports/{result.Value!.ImportId}", result.Value)
                : Problem(result.Error!.Code, result.Error.Message, StatusFor(result.Error.Code));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(FileRules.MaxBytes + 1024 * 1024));

        api.MapGet("/shops/{shopId:guid}/stock/imports/{importId:guid}", async (Guid shopId, Guid importId, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new GetStockImport(shopId, importId), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!.Code, result.Error.Message, 404);
        });

        api.MapPost("/shops/{shopId:guid}/stock/imports/{importId:guid}/remap", async (Guid shopId, Guid importId, RemapStockImportRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new RemapStockImport(shopId, importId, req), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result.Error!.Code, result.Error.Message, StatusFor(result.Error.Code));
        });

        api.MapPost("/shops/{shopId:guid}/stock/imports/{importId:guid}/confirm", async (Guid shopId, Guid importId, ConfirmStockImportRequest req, IDispatcher d, CancellationToken ct) =>
        {
            try
            {
                var result = await d.Send(new ConfirmStockImport(shopId, importId, req), ct);
                return result.IsSuccess ? Results.Ok(new { movements = result.Value }) : Problem(result.Error!.Code, result.Error.Message, StatusFor(result.Error.Code));
            }
            catch (DbUpdateConcurrencyException)
            {
                // Two taps at once: the other one won.
                return Problem("import.already_confirmed", "This delivery has already been received.", 409);
            }
        });
    }

    private static int StatusFor(string code) => code switch
    {
        "import.not_found" => 404,
        "import.already_confirmed" or "import.not_ready" or "import.not_remappable" => 409,
        "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" or "permission.denied" => 403,
        "file.too_large" or "import.too_large" => 413,
        _ => 422,
    };

    private static IResult Problem(string code, string message, int status) =>
        Results.Problem(title: message, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>Reads queued invoice photos and PDFs in the worker, a few at a time.</summary>
public sealed class InvoiceReadingService(IServiceScopeFactory scopes, ILogger<InvoiceReadingService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var busy = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new ReadQueuedInvoices(), stoppingToken);
                busy = result.IsSuccess && result.Value > 0;
                if (busy) log.LogInformation("Read {Count} queued invoices", result.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Reading queued invoices failed");
            }
            if (!busy) await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

/// <summary>
/// Development only: stands in for a real invoice reader so the whole flow can be tried. It returns one fixed line and
/// is never registered outside Development; elsewhere a queued invoice fails with a clear "no reader connected" message.
/// </summary>
internal sealed class DevInvoiceReader : IInvoiceReader
{
    public Task<InvoiceReading> Read(StoredFile file, CancellationToken ct) =>
        Task.FromResult(new InvoiceReading(true, [new RawImportLine(1, "Sample item from the development reader", null, 1, null, null, 100)], "Dev supplier", "DEV-1"));
}

public static class ImportHostingExtensions
{
    /// <summary>Reads queued invoices. Call this from the worker only.</summary>
    public static IServiceCollection AddInvoiceReading(this IServiceCollection services) => services.AddHostedService<InvoiceReadingService>();

    /// <summary>Development only: a fake invoice reader.</summary>
    public static IServiceCollection AddDevInvoiceReader(this IServiceCollection services) => services.AddSingleton<IInvoiceReader, DevInvoiceReader>();
}
