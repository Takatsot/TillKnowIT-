using System.Text.Json;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Infrastructure;

public static class MarketplaceEndpoints
{
    // TODO(auth): the caller and admin person ids should come from the signed-in person, not the request.
    public static IEndpointRouteBuilder MapMarketplaceEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/suppliers/applications", async (ApplyAsSupplierRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new ApplyAsSupplier(req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/suppliers/{result.Value!.SupplierId}", result.Value) : Problem(result.Error!);
        });

        api.MapGet("/admin/suppliers/pending", async (Guid adminPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListPendingSuppliers(adminPersonId), ct)));

        api.MapPost("/admin/suppliers/{id:guid}/verify", async (Guid id, VerifySupplierRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new VerifySupplier(id, req), ct)));

        api.MapPost("/admin/suppliers/{id:guid}/reject", async (Guid id, RejectSupplierRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new RejectSupplier(id, req), ct)));

        api.MapGet("/suppliers/{id:guid}", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetSupplier(id, callerPersonId), ct)));

        api.MapPut("/suppliers/{id:guid}/areas", async (Guid id, SetAreasRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new SetSupplierAreas(id, req), ct)));

        api.MapPut("/suppliers/{id:guid}/terms", async (Guid id, SetTermsRequest req, IDispatcher d, CancellationToken ct) => Done(await d.Send(new SetSupplierTerms(id, req), ct)));

        api.MapPost("/suppliers/{id:guid}/reps", async (Guid id, InviteRepRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new InviteRep(id, req), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/suppliers/{id}", new { membershipId = result.Value }) : Problem(result.Error!);
        });

        // Form fields: callerPersonId, apply (true to save, otherwise preview), mapping (optional JSON, field -> column header), file.
        api.MapPost("/suppliers/{id:guid}/products/import", async (Guid id, HttpRequest request, IDispatcher d, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Problem(new Error("import.form_required", "Send the file as a form upload."), 415);
            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null) return Problem(new Error("file.missing", "Attach the file as 'file'."));
            if (!Guid.TryParse(form["callerPersonId"], out var caller)) return Problem(new Error("import.caller_required", "callerPersonId is required."));
            Dictionary<string, string>? mapping = null;
            if (!string.IsNullOrWhiteSpace(form["mapping"]))
            {
                try { mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(form["mapping"].ToString()); }
                catch (JsonException) { return Problem(new Error("import.mapping_invalid", "mapping must be a JSON object.")); }
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var apply = string.Equals(form["apply"], "true", StringComparison.OrdinalIgnoreCase);
            return Reply(await d.Send(new ImportSupplierCatalogue(id, caller, Path.GetFileName(file.FileName), buffer.ToArray(), mapping, apply), ct));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(6 * 1024 * 1024));

        api.MapGet("/suppliers/{id:guid}/products", async (Guid id, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListSupplierProducts(id, callerPersonId), ct)));

        api.MapPatch("/supplier-products/{id:guid}", async (Guid id, UpdateSupplierProductRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new UpdateSupplierProduct(id, req), ct)));

        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Done(Result<Unit> result) => result.IsSuccess ? Results.NoContent() : Problem(result.Error!);

    private static IResult Problem(Error error, int? status = null) => Results.Problem(title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: status ?? error.Code switch
        {
            "permission.denied" => 403,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) => 404,
            "staff.already_member" or "supplier.not_pending" or "supplier.suspended" or "supplier.checklist_incomplete" => 409,
            _ => 422,
        });
}
