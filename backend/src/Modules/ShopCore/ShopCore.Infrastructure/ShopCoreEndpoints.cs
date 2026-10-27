using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShopCore.Application.Credit;
using ShopCore.Application.Insights;
using ShopCore.Application.Reports;
using ShopCore.Application.Stock;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;

namespace ShopCore.Infrastructure;

public static class ShopCoreEndpoints
{
    // TODO(auth): the till should authenticate. For now the device and staff ids in the body are checked
    // against Identity (approved device, active staff, permission) but nothing proves the caller owns them.
    public static IEndpointRouteBuilder MapShopCoreEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/sync/upload", async (SyncUploadRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new UploadBatch(req), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: result.Error!.Message, statusCode: StatusCodes.Status413PayloadTooLarge,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
        });

        api.MapPost("/products", (SaveProductRequest req, IDispatcher d, CancellationToken ct) =>
            SaveProduct(req, d, created: true, ct));

        api.MapPatch("/products/{id:guid}", (Guid id, SaveProductRequest req, IDispatcher d, CancellationToken ct) =>
            req.Product.Id != id
                ? Task.FromResult(Problem("product.id_mismatch", "The product id in the body does not match the address.", StatusCodes.Status422UnprocessableEntity))
                : SaveProduct(req, d, created: false, ct));

        api.MapGet("/shops/{shopId:guid}/credit-accounts", async (Guid shopId, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new ListCreditAccounts(shopId), ct);
            return Results.Ok(result.Value);
        });

        api.MapGet("/shops/{shopId:guid}/credit-accounts/{accountId:guid}/entries", async (Guid shopId, Guid accountId, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new GetCreditHistory(shopId, accountId), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Problem(result.Error!.Code, result.Error.Message, StatusCodes.Status404NotFound);
        });

        api.MapGet("/shops/{shopId:guid}/stock", async (Guid shopId, string? filter, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new ListStock(shopId, filter), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Problem(result.Error!.Code, result.Error.Message, StatusCodes.Status422UnprocessableEntity);
        });

        api.MapGet("/shops/{shopId:guid}/insights", async (Guid shopId, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new GetInsights(shopId), ct);
            return Results.Ok(result.Value);
        });

        api.MapGet("/shops/{shopId:guid}/reports/{code}", async (Guid shopId, string code, DateOnly? from, DateOnly? to, string? format, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new RunReport(shopId, code, from, to, format), ct);
            return result.IsSuccess
                ? Results.File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
                : Problem(result.Error!.Code, result.Error.Message, StatusCodes.Status422UnprocessableEntity);
        });

        ComplianceEndpoints.Map(api);
        SlipEndpoints.Map(api);
        ImportEndpoints.Map(api);
        VasEndpoints.Map(api);
        api.MapSellingEndpoints();
        return api;
    }

    private static async Task<IResult> SaveProduct(SaveProductRequest req, IDispatcher d, bool created, CancellationToken ct)
    {
        var batch = new SyncUploadRequest(req.ShopId, req.DeviceId, null, null, null, [req.Product]);
        var result = await d.Send(new UploadBatch(batch), ct);
        if (!result.IsSuccess) return Problem(result.Error!.Code, result.Error.Message, StatusCodes.Status422UnprocessableEntity);

        var outcome = result.Value!.Results.Single();
        return outcome.Status switch
        {
            UploadStatus.Accepted => created
                ? Results.Created($"/api/v1/products/{outcome.Id}", outcome)
                : Results.Ok(outcome),
            // A newer change is already saved. The till should take the server's copy.
            UploadStatus.Duplicate => Problem(outcome.Code ?? "product.stale", outcome.Message ?? "Already saved.", StatusCodes.Status409Conflict),
            _ => Problem(outcome.Code ?? "product.rejected", outcome.Message ?? "Rejected.", RejectedStatus(outcome.Code)),
        };
    }

    private static int RejectedStatus(string? code) => code switch
    {
        "product.duplicate_barcode" or "product.id_taken" => StatusCodes.Status409Conflict,
        "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" or "permission.denied" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    private static IResult Problem(string code, string message, int status) =>
        Results.Problem(title: message, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
