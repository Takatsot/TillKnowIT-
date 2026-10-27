using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShopCore.Application.Selling;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Infrastructure;

/// <summary>
/// The Phase 4 selling endpoints. Setup is by a signed-in person (callerPersonId); counter work is by a till and a cashier (deviceId, staffId).
/// Every one of them is switched on by the shop's business pack, so a spaza shop gets "feature.not_enabled" from all of them.
/// </summary>
internal static class SellingEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists.
    public static IEndpointRouteBuilder MapSellingEndpoints(this IEndpointRouteBuilder api)
    {
        // ---- setup ----
        api.MapPost("/shops/{shopId:guid}/products/{productId:guid}/variants", async (Guid shopId, Guid productId, CreateVariantsRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new CreateVariants(shopId, productId, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/shops/{shopId}/products/{productId}/variants", result.Value) : Problem(result.Error!);
        });
        api.MapGet("/shops/{shopId:guid}/products/{productId:guid}/variants", async (Guid shopId, Guid productId, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListVariants(shopId, productId, callerPersonId), ct)));
        api.MapPut("/shops/{shopId:guid}/products/{productId:guid}/selling-flags", async (Guid shopId, Guid productId, SellingFlagsRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new SetSellingFlags(shopId, productId, req), ct)));

        api.MapPost("/shops/{shopId:guid}/modifier-groups", async (Guid shopId, ModifierGroupRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new SaveModifierGroup(shopId, null, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/shops/{shopId}/modifier-groups", result.Value) : Problem(result.Error!);
        });
        api.MapPut("/shops/{shopId:guid}/modifier-groups/{groupId:guid}", async (Guid shopId, Guid groupId, ModifierGroupRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SaveModifierGroup(shopId, groupId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/modifier-groups", async (Guid shopId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListModifierGroups(shopId, deviceId, staffId), ct)));
        api.MapPut("/shops/{shopId:guid}/products/{productId:guid}/modifier-groups", async (Guid shopId, Guid productId, AssignModifierGroupsRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new AssignModifierGroups(shopId, productId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/products/{productId:guid}/modifier-groups", async (Guid shopId, Guid productId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetProductModifiers(shopId, productId, deviceId, staffId), ct)));
        api.MapPost("/shops/{shopId:guid}/products/{productId:guid}/modifiers/price", async (Guid shopId, Guid productId, ValidateModifiersRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ValidateModifiers(shopId, productId, req), ct)));

        api.MapPost("/shops/{shopId:guid}/promotions", async (Guid shopId, PromotionRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new SavePromotion(shopId, null, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/shops/{shopId}/promotions", result.Value) : Problem(result.Error!);
        });
        api.MapPatch("/shops/{shopId:guid}/promotions/{id:guid}", async (Guid shopId, Guid id, PromotionRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SavePromotion(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/promotions/{id:guid}/active", async (Guid shopId, Guid id, SetActiveRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SetPromotionActive(shopId, id, req), ct)));
        api.MapGet("/shops/{shopId:guid}/promotions", async (Guid shopId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListPromotions(shopId, deviceId, staffId), ct)));
        api.MapPost("/shops/{shopId:guid}/price-basket", async (Guid shopId, PriceBasketRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new PriceBasket(shopId, req), ct)));

        api.MapPut("/shops/{shopId:guid}/floor-plan", async (Guid shopId, FloorPlanRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SaveFloorPlan(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/floor-plan", async (Guid shopId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetFloorPlan(shopId, deviceId, staffId), ct)));

        // ---- open tickets ----
        api.MapPost("/shops/{shopId:guid}/tickets", async (Guid shopId, OpenTicketRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new OpenTicketCommand(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/tickets", async (Guid shopId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListTickets(shopId, deviceId, staffId), ct)));
        api.MapPost("/shops/{shopId:guid}/tickets/{id:guid}/hold", async (Guid shopId, Guid id, TillCall call, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new HoldTicket(shopId, id, call), ct)));
        api.MapPost("/shops/{shopId:guid}/tickets/{id:guid}/release", async (Guid shopId, Guid id, TillCall call, IDispatcher d, CancellationToken ct) => Done(await d.Send(new ReleaseTicket(shopId, id, call), ct)));
        api.MapPut("/shops/{shopId:guid}/tickets/{id:guid}", async (Guid shopId, Guid id, EditTicketRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new EditTicket(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/tickets/{id:guid}/move", async (Guid shopId, Guid id, MoveTicketRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new MoveTicket(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/tickets/{id:guid}/merge", async (Guid shopId, Guid id, MergeTicketRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new MergeTickets(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/tickets/{id:guid}/close", async (Guid shopId, Guid id, CloseTicketRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CloseTicket(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/tickets/{id:guid}/void", async (Guid shopId, Guid id, TillCall call, IDispatcher d, CancellationToken ct) => Done(await d.Send(new VoidTicket(shopId, id, call), ct)));

        // ---- the kitchen ----
        api.MapPost("/shops/{shopId:guid}/kitchen-orders", async (Guid shopId, CreateKitchenOrderRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CreateKitchenOrderCommand(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/kitchen-orders", async (Guid shopId, Guid deviceId, Guid staffId, string? status, DateTimeOffset? since, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListKitchenOrders(shopId, deviceId, staffId, status, since), ct)));
        api.MapPost("/shops/{shopId:guid}/kitchen-orders/{id:guid}/advance", async (Guid shopId, Guid id, AdvanceKitchenOrderRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new AdvanceKitchenOrder(shopId, id, req), ct)));

        // ---- quotes ----
        api.MapPost("/shops/{shopId:guid}/quotes", async (Guid shopId, CreateQuoteRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CreateQuoteCommand(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/quotes", async (Guid shopId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListQuotes(shopId, deviceId, staffId), ct)));
        api.MapGet("/shops/{shopId:guid}/quotes/{id:guid}/pdf", async (Guid shopId, Guid id, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new GetQuotePdf(shopId, id, deviceId, staffId), ct);
            return result.IsSuccess ? Results.File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error!);
        });
        api.MapPost("/shops/{shopId:guid}/quotes/{id:guid}/convert", async (Guid shopId, Guid id, TillCall call, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ConvertQuote(shopId, id, call), ct)));
        api.MapPost("/shops/{shopId:guid}/quotes/{id:guid}/cancel", async (Guid shopId, Guid id, TillCall call, IDispatcher d, CancellationToken ct) => Done(await d.Send(new CancelQuote(shopId, id, call), ct)));

        // ---- lay-by ----
        api.MapPost("/shops/{shopId:guid}/laybys", async (Guid shopId, CreateLaybyRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CreateLayby(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/laybys", async (Guid shopId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListLaybys(shopId, deviceId, staffId), ct)));
        api.MapPost("/shops/{shopId:guid}/laybys/{id:guid}/payments", async (Guid shopId, Guid id, PayLaybyRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new PayLayby(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/laybys/{id:guid}/cancel", async (Guid shopId, Guid id, TillCall call, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CancelLayby(shopId, id, call), ct)));
        api.MapPost("/shops/{shopId:guid}/laybys/{id:guid}/collect", async (Guid shopId, Guid id, CollectLaybyRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CollectLayby(shopId, id, req), ct)));
        api.MapGet("/shops/{shopId:guid}/laybys/reservations", async (Guid shopId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetLaybyReservations(shopId, deviceId, staffId), ct)));

        // ---- vouchers ----
        api.MapPost("/shops/{shopId:guid}/vouchers", async (Guid shopId, IssueVoucherRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new IssueVoucher(shopId, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/shops/{shopId}/vouchers", result.Value) : Problem(result.Error!);
        });
        api.MapPost("/shops/{shopId:guid}/vouchers/check", async (Guid shopId, CheckVoucherRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new CheckVoucher(shopId, req.Code, req.DeviceId, req.StaffId), ct)));
        api.MapPost("/shops/{shopId:guid}/vouchers/redeem", async (Guid shopId, RedeemVoucherRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RedeemVoucher(shopId, req), ct)));

        // ---- serial numbers ----
        api.MapPost("/shops/{shopId:guid}/sales/{saleId:guid}/serials", async (Guid shopId, Guid saleId, RecordSerialsRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new RecordSerials(shopId, saleId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/serials", async (Guid shopId, string text, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new FindSerials(shopId, text, deviceId, staffId), ct)));

        // ---- refunds ----
        api.MapPost("/shops/{shopId:guid}/sales/{saleId:guid}/refunds", async (Guid shopId, Guid saleId, OnlineRefundRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new CreateOnlineRefund(shopId, saleId, req), ct)));
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Done(Result<Unit> result) => result.IsSuccess ? Results.NoContent() : Problem(result.Error!);

    private static IResult Problem(Error error) => Results.Problem(title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: error.Code switch
        {
            "permission.denied" or "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" or "feature.not_enabled" => 403,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) => 404,
            "ticket.locked" or "ticket.not_held" or "ticket.not_open" or "kitchen.order_no_taken" or "variant.barcode_taken" or "variant.label_repeated" or "serial.already_sold"
                or "refund.already_processed" or "layby.not_active" or "quote.not_open" or "voucher.already_issued" => 409,
            _ => 422,
        });
}

public sealed record CheckVoucherRequest(string Code, Guid DeviceId, Guid StaffId);
