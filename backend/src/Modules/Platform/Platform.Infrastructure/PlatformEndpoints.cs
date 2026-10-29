using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Platform.Application;
using Platform.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Platform.Infrastructure;

public static class PlatformEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists.
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/business-packs", async (IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListBusinessPacks(), ct)));
        api.MapGet("/shops/{shopId:guid}/business-pack", async (Guid shopId, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetShopPack(shopId, callerPersonId), ct)));
        api.MapGet("/shops/{shopId:guid}/devices/{deviceId:guid}/business-pack", async (Guid shopId, Guid deviceId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetDevicePack(shopId, deviceId), ct)));
        api.MapPut("/shops/{shopId:guid}/business-pack", async (Guid shopId, ChoosePackRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ChooseShopPack(shopId, req), ct)));

        api.MapGet("/admin/business-packs", async (Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListAllBusinessPacks(callerPersonId), ct)));
        api.MapPost("/admin/business-packs", async (SavePackRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new SavePack(null, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/admin/business-packs/{result.Value!.Code}", result.Value) : Problem(result.Error!);
        });
        api.MapPut("/admin/business-packs/{code}", async (string code, SavePackRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SavePack(code, req), ct)));
        api.MapPost("/admin/business-packs/{code}/active", async (string code, Guid callerPersonId, bool active, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SetPackActive(code, callerPersonId, active), ct)));
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Problem(Error error) => Results.Problem(title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: error.Code switch
        {
            "permission.denied" => 403,
            "pack.not_found" => 404,
            "pack.code_taken" or "pack.default_required" => 409,
            _ => 422,
        });
}
