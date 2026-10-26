using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        return api;
    }
}
