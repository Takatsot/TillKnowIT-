using Insights.Application;
using Insights.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Insights.Infrastructure;

public static class InsightsEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists.
    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPut("/shops/{shopId:guid}/insights-opt-in", async (Guid shopId, SetOptInRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SetInsightOptIn(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/insights-opt-in", async (Guid shopId, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetInsightOptIn(shopId, personId), ct)));
        api.MapGet("/insights/reports", async (Guid personId, Guid organisationId, string audience, string area, string? category, DateOnly from, DateOnly to, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Ask(new GetInsightReport(new(personId, organisationId, audience, area, category, from, to)), ct)));
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(title: result.Error!.Message,
        extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code },
        statusCode: result.Error.Code switch { "permission.denied" or "plan.feature_not_included" => 403, _ => 422 });
}
