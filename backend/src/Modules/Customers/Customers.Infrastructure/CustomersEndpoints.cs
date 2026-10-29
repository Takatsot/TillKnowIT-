using Customers.Application;
using Customers.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Customers.Infrastructure;

public static class CustomersEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists.
    public static IEndpointRouteBuilder MapCustomersEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/shops/{shopId:guid}/customers", async (Guid shopId, CreateCustomerRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CreateCustomer(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/customers", async (Guid shopId, string search, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new FindCustomers(shopId, deviceId, staffId, search), ct)));
        api.MapGet("/shops/{shopId:guid}/customers/{id:guid}", async (Guid shopId, Guid id, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetCustomer(shopId, id, deviceId, staffId), ct)));
        api.MapPut("/shops/{shopId:guid}/customers/{id:guid}", async (Guid shopId, Guid id, UpdateCustomerRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new UpdateCustomer(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/customers/{id:guid}/merge", async (Guid shopId, Guid id, MergeCustomersRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new MergeCustomers(shopId, id, req), ct)));
        api.MapGet("/shops/{shopId:guid}/customers/{id:guid}/points", async (Guid shopId, Guid id, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetPoints(shopId, id, deviceId, staffId), ct)));
        api.MapPost("/shops/{shopId:guid}/customers/{id:guid}/points/redeem", async (Guid shopId, Guid id, RedeemPointsRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RedeemPoints(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/customers/{id:guid}/points/release", async (Guid shopId, Guid id, ReleasePointsRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ReleasePoints(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/customers/{id:guid}/points/adjust", async (Guid shopId, Guid id, AdjustPointsRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new AdjustPoints(shopId, id, req), ct)));
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(title: result.Error!.Message,
        extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code },
        statusCode: result.Error.Code switch
        {
            "permission.denied" or "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" or "feature.not_enabled" => 403,
            "customer.not_found" => 404,
            "customer.phone_taken" or "customer.merged" => 409,
            _ => 422,
        });
}
