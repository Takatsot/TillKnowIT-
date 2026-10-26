using Identity.Application.Devices;
using Identity.Application.Shops;
using Identity.Application.Staff;
using Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Infrastructure;

public static class IdentityEndpoints
{
    // TODO(auth): none of these check who is calling yet. Once OTP sign-in exists, require an owner or
    // a staff.manage permission on the shop, and take the owner from the signed-in person.
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/shops", async (CreateShopRequest req, Guid ownerPersonId, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new CreateShop(ownerPersonId, req.Name, req.Area, req.Language), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/shops/{result.Value}", new CreateShopResponse(result.Value))
                : Problem(result);
        });

        api.MapPost("/shops/{shopId:guid}/staff", async (Guid shopId, AddStaffRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new AddStaff(shopId, req.Name, req.Phone, req.RoleCode, req.Pin), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/staff/{result.Value}", new AddStaffResponse(result.Value))
                : Problem(result);
        });

        api.MapPatch("/staff/{id:guid}", async (Guid id, UpdateStaffRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new UpdateStaff(id, req.Pin, req.Disabled), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result);
        });

        api.MapPost("/staff/{id:guid}/verify-pin", async (Guid id, VerifyPinRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new VerifyPin(id, req.Pin), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result);
        });

        api.MapPost("/shops/{shopId:guid}/devices", async (Guid shopId, RegisterDeviceRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new RegisterDevice(shopId, req.Name), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/devices/{result.Value}", new RegisterDeviceResponse(result.Value))
                : Problem(result);
        });

        api.MapPost("/devices/{id:guid}/approve", async (Guid id, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new ApproveDevice(id), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result);
        });

        api.MapPost("/devices/{id:guid}/wipe", async (Guid id, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new WipeDevice(id), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result);
        });

        api.MapPost("/devices/{id:guid}/check-in", async (Guid id, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new CheckInDevice(id), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Problem(result);
        });

        api.MapPost("/devices/{id:guid}/wipe-confirmed", async (Guid id, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new ConfirmDeviceWiped(id), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result);
        });

        return api;
    }

    private static IResult Problem(Result result)
    {
        var error = result.Error!;
        var status = error.Code switch
        {
            "staff.pin_rejected" => StatusCodes.Status401Unauthorized,
            "staff.locked" => StatusCodes.Status423Locked,
            "staff.already_member" => StatusCodes.Status409Conflict,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status422UnprocessableEntity,
        };
        return Results.Problem(title: error.Message, statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
