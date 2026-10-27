using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Slips;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.Dispatching;

namespace ShopCore.Infrastructure;

internal sealed class SlipStore(ShopCoreDbContext db) : ISlipStore
{
    public async Task<SlipSale?> GetSale(Guid saleId, CancellationToken ct)
    {
        var sale = await db.Sales.AsNoTracking().Where(s => s.Id == saleId)
            .Select(s => new
            {
                s.Id, s.ShopId, s.SoldAt, s.TotalCents, s.VatCents, s.PaymentMethodCode,
                Cancelled = db.Set<SaleCancellation>().Any(c => c.SaleId == s.Id),
                Lines = s.Lines.Select(l => new { l.Name, l.Quantity, l.PriceCents, l.UnitDivisor, l.DiscountCents }).ToList(),
            }).FirstOrDefaultAsync(ct);
        return sale is null ? null : new SlipSale(sale.Id, sale.ShopId, sale.SoldAt, sale.TotalCents, sale.VatCents, sale.PaymentMethodCode, sale.Cancelled,
            sale.Lines.Select(l => new SlipLine(l.Name, l.Quantity, l.PriceCents, l.UnitDivisor, l.DiscountCents)).ToList());
    }

    public Task<int> CountRequests(Guid saleId, CancellationToken ct) =>
        db.Set<SlipRequest>().AsNoTracking().CountAsync(s => s.SaleId == saleId, ct);

    public async Task Add(SlipRequest slip, CancellationToken ct) => await db.Set<SlipRequest>().AddAsync(slip, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal static class SlipEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/sales/{saleId:guid}/slip", async (Guid saleId, RequestSlipRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new RequestSlip(saleId, req), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: result.Error!.Message, extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code },
                    statusCode: result.Error.Code switch
                    {
                        "sale.not_found" => 404,
                        "slip.limit_reached" => 429,
                        "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" or "permission.denied" => 403,
                        _ => 422,
                    });
        });

        // The public slip page behind the QR code. It needs no sign-in: the signed token is the credential.
        api.MapGet("/slips/{token}", async (string token, HttpContext http, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new GetSlip(token), ct);
            if (!result.IsSuccess) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=300";
            return Results.Ok(result.Value);
        });
    }
}
