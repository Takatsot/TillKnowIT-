using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Payments.Application;
using Payments.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Payments.Infrastructure;

public static class PaymentsEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists. Caller ids are in the request until then.
    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/purchase-orders/{id:guid}/payments", async (Guid id, StartPaymentRequest req, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Send(new StartPayment(id, req), ct)));

        api.MapGet("/purchase-orders/{id:guid}/payments", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Ask(new GetOrderPayment(id, callerPersonId), ct)));

        // The partner calls this. The body is read as it came, because the signature covers those exact bytes.
        api.MapPost("/payments/webhooks/{partner}", async (string partner, HttpRequest request, IDispatcher d, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await d.Send(new HandlePaymentWebhook(partner, request.Headers["X-Signature"].ToString(), body), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result.Error!, result.Error!.Code == "webhook.signature_invalid" ? 401 : null);
        });

        api.MapPost("/purchase-orders/{id:guid}/disputes", async (Guid id, RaiseDisputeRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new RaiseDispute(id, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/admin/disputes", result.Value) : Problem(result.Error!);
        });

        api.MapGet("/admin/disputes", async (Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListOpenDisputes(callerPersonId), ct)));

        api.MapPost("/admin/disputes/{id:guid}/resolve", async (Guid id, ResolveDisputeRequest req, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Send(new ResolveDispute(id, req), ct)));

        api.MapPost("/admin/payments/reconcile", async (DateOnly day, Guid callerPersonId, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Send(new ReconcilePayments(day, callerPersonId), ct)));

        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Problem(Error error, int? status = null) => Results.Problem(title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: status ?? error.Code switch
        {
            "permission.denied" => 403,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) || c == "payment.unknown" => 404,
            "payment.already_paid" or "payment.not_held" or "payment.not_pending" or "dispute.already_open" or "dispute.already_resolved" => 409,
            "payment.partner_not_connected" => 503,
            _ => 422,
        });
}
