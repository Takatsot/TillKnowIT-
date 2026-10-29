using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TillKnowIT.Dispatching;
using TillKnowIT.Reporting;
using TillKnowIT.SharedKernel;
using TillKnowIT.Storage;
using Wallet.Application;
using Wallet.Contracts;

namespace Wallet.Infrastructure;

public static class WalletEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists. Caller ids are in the request until then.
    public static IEndpointRouteBuilder MapWalletEndpoints(this IEndpointRouteBuilder api)
    {
        // ---- schools ----
        api.MapPost("/admin/schools", async (RegisterSchoolRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new RegisterSchool(req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/schools/{result.Value!.Id}", result.Value) : Problem(result.Error!);
        });
        api.MapPost("/admin/schools/{id:guid}/verify", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new VerifySchool(id, callerPersonId), ct)));
        api.MapPut("/schools/{id:guid}/settings", async (Guid id, ConfigureSchoolRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ConfigureSchool(id, req), ct)));
        api.MapPost("/schools/{id:guid}/admins", async (Guid id, AddSchoolAdminRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new AddSchoolAdmin(id, req), ct)));
        api.MapGet("/schools/{id:guid}", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetSchool(id, callerPersonId), ct)));

        // ---- holders ----
        api.MapPost("/schools/{id:guid}/holders", async (Guid id, AddHolderRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new AddHolder(id, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/schools/{id}/holders", result.Value) : Problem(result.Error!);
        });
        api.MapGet("/schools/{id:guid}/holders", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListHolders(id, callerPersonId), ct)));

        // Form fields: callerPersonId, file (.xlsx or .csv).
        api.MapPost("/schools/{id:guid}/holders/import", async (Guid id, HttpRequest request, IDispatcher d, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Problem(new Error("import.form_required", "Send the file as a form upload."));
            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null) return Problem(new Error("file.missing", "Attach the file as 'file'."));
            if (!Guid.TryParse(form["callerPersonId"], out var caller)) return Problem(new Error("import.caller_required", "callerPersonId is required."));
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            return Reply(await d.Send(new ImportHolders(id, caller, Path.GetFileName(file.FileName), buffer.ToArray()), ct));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(SheetLimit));

        api.MapPatch("/holders/{id:guid}", async (Guid id, UpdateHolderRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new UpdateHolder(id, req), ct)));
        api.MapPost("/holders/{id:guid}/left", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Done(await d.Send(new MarkHolderLeft(id, callerPersonId), ct)));
        api.MapPost("/holders/{id:guid}/link-code", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new NewLinkCode(id, callerPersonId), ct)));

        // Form fields: callerPersonId, file (JPEG or PNG).
        api.MapPut("/holders/{id:guid}/photo", async (Guid id, HttpRequest request, IDispatcher d, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Problem(new Error("import.form_required", "Send the photo as a form upload."));
            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null) return Problem(new Error("file.missing", "Attach the photo as 'file'."));
            if (!Guid.TryParse(form["callerPersonId"], out var caller)) return Problem(new Error("import.caller_required", "callerPersonId is required."));
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            return Done(await d.Send(new SetHolderPhoto(id, caller, file.ContentType, buffer.ToArray()), ct));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(FileRules.MaxBytes + 1024 * 1024));

        // Only a till of the learner's own school, with a signed-in cashier, can open a photo.
        api.MapGet("/holders/{id:guid}/photo", async (Guid id, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new GetHolderPhoto(id, deviceId, staffId), ct);
            return result.IsSuccess ? Results.File(result.Value!.Content, result.Value.ContentType) : Problem(result.Error!);
        });

        // ---- cards ----
        api.MapPost("/holders/{id:guid}/tokens", async (Guid id, IssueTokenRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new IssueToken(id, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/holders/{id}/tokens", result.Value) : Problem(result.Error!);
        });
        api.MapGet("/admin/schools", async (Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListSchools(callerPersonId), ct)));
        api.MapGet("/admin/wallet-reconciliations", async (Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListReconciliations(callerPersonId), ct)));
        api.MapGet("/holders/{id:guid}/tokens", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListHolderTokens(id, callerPersonId), ct)));
        api.MapPost("/tokens/{id:guid}/block", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Done(await d.Send(new BlockToken(id, callerPersonId), ct)));
        api.MapPost("/holders/{id:guid}/block-cards", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new BlockMyChildsCards(id, callerPersonId), ct)));

        // ---- parents ----
        api.MapPost("/guardianships", async (LinkChildRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new LinkChild(req), ct)));
        api.MapGet("/me/children", async (Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetMyChildren(callerPersonId), ct)));

        // ---- wallets ----
        api.MapGet("/wallets/{id:guid}", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetWallet(id, callerPersonId), ct)));
        api.MapPut("/wallets/{id:guid}/limits", async (Guid id, SetLimitsRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SetWalletLimits(id, req), ct)));
        api.MapPut("/wallets/{id:guid}/restrictions", async (Guid id, SetRestrictionsRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SetWalletRestrictions(id, req), ct)));
        api.MapGet("/wallets/{id:guid}/qr", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetWalletQr(id, callerPersonId), ct)));

        // Without a format this is the statement as JSON; with csv, xlsx or pdf it is a download.
        api.MapGet("/wallets/{id:guid}/statement", async (Guid id, Guid callerPersonId, DateOnly? from, DateOnly? to, string? format, IDispatcher d, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(format)) return Reply(await d.Ask(new GetWalletStatement(id, callerPersonId, from, to), ct));
            var file = await d.Ask(new ExportWalletStatement(id, callerPersonId, from, to, format), ct);
            return file.IsSuccess ? Results.File(file.Value!.Content, file.Value.ContentType, file.Value.FileName) : Problem(file.Error!);
        });

        // ---- money in ----
        api.MapPost("/wallets/{id:guid}/top-ups", async (Guid id, StartTopUpRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new StartTopUp(id, req), ct)));
        api.MapPost("/schools/{id:guid}/cash-top-ups", async (Guid id, CashTopUpRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new RecordCashTopUp(id, req), ct)));
        api.MapPost("/admin/schools/{id:guid}/cash-deposits", async (Guid id, Guid callerPersonId, long amountCents, string reference, IDispatcher d, CancellationToken ct) =>
            Done(await d.Send(new RecordCashDeposit(id, callerPersonId, amountCents, reference), ct)));
        api.MapPost("/wallets/transfers", async (TransferRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new TransferBetweenChildren(req), ct)));

        // The partner calls this. The body is read as it came, because the signature covers those exact bytes.
        api.MapPost("/wallet/webhooks/{partner}", async (string partner, HttpRequest request, IDispatcher d, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await d.Send(new HandleWalletWebhook(partner, request.Headers["X-Signature"].ToString(), body), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result.Error!, result.Error!.Code == "webhook.signature_invalid" ? 401 : null);
        });

        // ---- at the till ----
        api.MapPost("/shops/{shopId:guid}/wallet-payments", async (Guid shopId, WalletPaymentRequest req, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Send(new ProcessWalletPayment(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/wallet-snapshot", async (Guid shopId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Ask(new GetTillSnapshot(shopId, deviceId, staffId), ct)));
        api.MapPost("/shops/{shopId:guid}/wallet-sync/upload", async (Guid shopId, SettleOfflineSalesRequest req, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Send(new SettleOfflineSales(shopId, req), ct)));

        // ---- menu and pre-orders ----
        api.MapPut("/schools/{id:guid}/breaks", async (Guid id, SaveBreaksRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new SaveBreaks(id, req), ct)));
        api.MapPost("/schools/{id:guid}/menu", async (Guid id, SaveMenuItemRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new AddMenuItem(id, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/schools/{id}/menu", result.Value) : Problem(result.Error!);
        });
        api.MapPut("/menu-items/{id:guid}", async (Guid id, SaveMenuItemRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ChangeMenuItem(id, req), ct)));
        api.MapGet("/schools/{id:guid}/menu", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetSchoolMenu(id, callerPersonId), ct)));
        api.MapGet("/holders/{id:guid}/menu", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetMenuForChild(id, callerPersonId), ct)));
        api.MapPost("/pre-orders", async (PlacePreOrderRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new PlacePreOrder(req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/pre-orders/{result.Value!.Id}", result.Value) : Problem(result.Error!);
        });
        api.MapPost("/pre-orders/{id:guid}/cancel", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Done(await d.Send(new CancelPreOrder(id, callerPersonId), ct)));
        api.MapGet("/holders/{id:guid}/pre-orders", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListChildPreOrders(id, callerPersonId), ct)));
        api.MapGet("/shops/{shopId:guid}/pre-orders", async (Guid shopId, Guid deviceId, Guid staffId, DateOnly? day, string? breakCode, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Ask(new ListPreOrdersForTill(shopId, deviceId, staffId, day, breakCode), ct)));
        api.MapPost("/shops/{shopId:guid}/pre-orders/{id:guid}/collect", async (Guid shopId, Guid id, TillPreOrderCall call, IDispatcher d, CancellationToken ct) =>
            Done(await d.Send(new CollectPreOrder(shopId, id, call), ct)));

        // ---- refunds, payouts, reconciliation, staff accounts, erasure ----
        api.MapPost("/wallets/{id:guid}/refund-requests", async (Guid id, RefundRequestDto req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RequestRefund(id, req), ct)));
        api.MapPost("/refunds/{id:guid}/review", async (Guid id, ReviewRefundRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ReviewRefund(id, req), ct)));
        api.MapPost("/admin/wallet-reconciliations", async (DateOnly day, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RunWalletReconciliation(day, callerPersonId), ct)));
        api.MapPost("/admin/wallet-reconciliations/{id:guid}/resolve", async (Guid id, ResolveReconciliationRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ResolveWalletReconciliation(id, req), ct)));
        api.MapPost("/admin/wallet-payouts/run", async (Guid callerPersonId, DateOnly? day, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RunPayouts(day, callerPersonId), ct)));
        api.MapGet("/shops/{shopId:guid}/wallet-payouts", async (Guid shopId, Guid callerPersonId, DateOnly? from, DateOnly? to, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Ask(new GetShopWalletSales(shopId, callerPersonId, from, to), ct)));
        api.MapGet("/schools/{id:guid}/staff-accounts/{month}", async (Guid id, string month, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetStaffMonth(id, callerPersonId, month), ct)));
        api.MapPost("/admin/schools/{id:guid}/staff-accounts/{month}/settle", async (Guid id, string month, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SettleStaffMonth(id, callerPersonId, month), ct)));
        api.MapPost("/holders/{id:guid}/erasure-requests", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RequestErasure(id, callerPersonId), ct)));

        return api;
    }

    private const long SheetLimit = 6 * 1024 * 1024;

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Done(Result<Unit> result) => result.IsSuccess ? Results.NoContent() : Problem(result.Error!);

    private static IResult Problem(Error error, int? status = null) => Results.Problem(title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: status ?? error.Code switch
        {
            "permission.denied" or "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" => 403,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) || c is "topup.unknown" => 404,
            "wallet.busy" or "sync.busy" or "sale.id_reused" or "token.already_linked" or "link.already_linked" or "holder.duplicate" or "school.shop_taken"
                or "refund.already_requested" or "refund.not_requested" or "reconciliation.not_open" or "preorder.not_cancellable" or "preorder.not_collectable" => 409,
            "token.blocked" or "wallet.frozen" or "wallet.item_blocked" or "wallet.daily_limit" or "wallet.insufficient" => 402,
            "payment.partner_not_connected" => 503,
            "file.too_large" => 413,
            _ => 422,
        });
}
