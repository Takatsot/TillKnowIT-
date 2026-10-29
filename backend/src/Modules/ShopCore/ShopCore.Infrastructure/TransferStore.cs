using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Stock;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Infrastructure;

internal sealed class TransferStore(ShopCoreDbContext db) : ITransferStore
{
    public void Add(StockTransfer transfer) => db.Set<StockTransfer>().Add(transfer);
    public void Add(StockMovement movement) => db.StockMovements.Add(movement);
    public Task<StockTransfer?> Get(Guid transferId, CancellationToken ct) => db.Set<StockTransfer>().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == transferId, ct);

    public Task<List<StockTransfer>> For(Guid shopId, string? status, int take, CancellationToken ct)
    {
        var parsed = status is null ? (TransferStatus?)null : Enum.Parse<TransferStatus>(status, ignoreCase: true);
        return db.Set<StockTransfer>().AsNoTracking().Include(t => t.Lines)
            .Where(t => (t.FromShopId == shopId || t.ToShopId == shopId) && (parsed == null || t.Status == parsed)).OrderByDescending(t => t.SentAt).Take(take).ToListAsync(ct);
    }

    public Task<List<TransferProduct>> Products(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        db.Products.AsNoTracking().Where(p => p.ShopId == shopId && ids.Contains(p.Id)).Select(p => new TransferProduct(p.Id, p.Name, p.Barcode, p.IsActive)).ToListAsync(ct);

    public Task<List<TransferProduct>> ByBarcodes(Guid shopId, IReadOnlyCollection<string> barcodes, CancellationToken ct) =>
        db.Products.AsNoTracking().Where(p => p.ShopId == shopId && p.IsActive && p.Barcode != null && barcodes.Contains(p.Barcode)).Select(p => new TransferProduct(p.Id, p.Name, p.Barcode, p.IsActive)).ToListAsync(ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public static class TransferEndpoints
{
    // TODO(auth): the till identifies itself with device and staff ids until sign-in exists.
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/shops/{shopId:guid}/products", async (Guid shopId, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListShopProducts(shopId, callerPersonId), ct)));
        api.MapGet("/shops/{shopId:guid}/sync/products", async (Guid shopId, Guid deviceId, DateTimeOffset? since, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Ask(new PullProducts(shopId, deviceId, since), ct)));
        api.MapPost("/shops/{shopId:guid}/stock-transfers", async (Guid shopId, SendTransferRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SendTransfer(shopId, req), ct)));
        api.MapPost("/shops/{shopId:guid}/stock-transfers/{id:guid}/receive", async (Guid shopId, Guid id, ReceiveTransferRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ReceiveTransfer(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/stock-transfers/{id:guid}/cancel", async (Guid shopId, Guid id, CancelTransferRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CancelTransfer(shopId, id, req), ct)));
        api.MapGet("/shops/{shopId:guid}/stock-transfers", async (Guid shopId, Guid deviceId, Guid staffId, string? status, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListTransfers(shopId, deviceId, staffId, status), ct)));
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(title: result.Error!.Message,
        extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code },
        statusCode: result.Error.Code switch
        {
            "permission.denied" or "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" => 403,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) => 404,
            "transfer.not_in_transit" => 409,
            _ => 422,
        });
}

internal sealed class StockTransferConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<StockTransfer> b)
    {
        b.ToTable("stock_transfers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.TransferId);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.FromShopId, x.Status });
        b.HasIndex(x => new { x.ToShopId, x.Status });
    }
}

internal sealed class StockTransferLineConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<StockTransferLine> b)
    {
        b.ToTable("stock_transfer_lines");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.Shortfall);
    }
}
