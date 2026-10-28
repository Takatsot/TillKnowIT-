using Billing.Application;
using Billing.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Billing.Infrastructure;

public static class BillingEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists.
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/plans", async (string? audience, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListPlans(audience), ct)));
        api.MapPut("/plans", async (SavePlanRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SavePlan(req), ct)));

        api.MapPost("/subscriptions", async (SubscribeRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new Subscribe(req), ct)));
        api.MapPatch("/organisations/{organisationId:guid}/subscription", async (Guid organisationId, ChangeSubscriptionRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ChangeSubscription(organisationId, req), ct)));
        api.MapGet("/organisations/{organisationId:guid}/subscription", async (Guid organisationId, Guid personId, string audience, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetSubscription(organisationId, personId, audience), ct)));
        api.MapGet("/organisations/{organisationId:guid}/invoices", async (Guid organisationId, Guid personId, string audience, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListInvoices(organisationId, personId, audience), ct)));
        api.MapPost("/invoices/{id:guid}/pay", async (Guid id, Guid personId, string audience, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new PayInvoice(id, personId, audience), ct)));

        api.MapPost("/sponsored-campaigns", async (CreateCampaignRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CreateCampaign(req), ct)));
        api.MapGet("/suppliers/{organisationId:guid}/sponsored-campaigns", async (Guid organisationId, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListCampaigns(organisationId, personId), ct)));
        api.MapPost("/sponsored-campaigns/{id:guid}/pause", async (Guid id, Guid personId, bool paused, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new PauseCampaign(id, personId, paused), ct)));
        api.MapGet("/sponsored-campaigns/{id:guid}/usage", async (Guid id, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetCampaignUsage(id, personId), ct)));
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Problem(Error error) => Results.Problem(title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: error.Code switch
        {
            "permission.denied" or "plan.feature_not_included" => 403,
            "payment.failed" => 402,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) => 404,
            "subscription.already_active" or "subscription.cancelled" or "subscription.same_plan" or "invoice.not_payable" or "campaign.ended" => 409,
            _ => 422,
        });
}
