using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ShopCore.Application.Compliance;
using TillKnowIT.Dispatching;
using TillKnowIT.Storage;

namespace ShopCore.Infrastructure;

internal static class ComplianceEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        // Form fields: deviceId, staffId, typeCode, expiresOn (optional, yyyy-MM-dd), file.
        api.MapPost("/shops/{shopId:guid}/compliance/documents", async (Guid shopId, HttpRequest request, IDispatcher d, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Problem("compliance.form_required", "Send the document as a form upload.", 415);
            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null) return Problem("file.missing", "Attach the document as 'file'.", 422);
            if (file.Length > FileRules.MaxBytes) return Problem("file.too_large", "The file is larger than 10 MB.", 413);
            if (!Guid.TryParse(form["deviceId"], out var deviceId) || !Guid.TryParse(form["staffId"], out var staffId))
                return Problem("compliance.caller_required", "deviceId and staffId are required.", 422);
            DateOnly? expiresOn = null;
            if (!string.IsNullOrWhiteSpace(form["expiresOn"]))
            {
                if (!DateOnly.TryParse(form["expiresOn"], out var parsed)) return Problem("compliance.expiry_invalid", "expiresOn must be yyyy-MM-dd.", 422);
                expiresOn = parsed;
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var result = await d.Send(new UploadComplianceDocument(shopId, deviceId, staffId, form["typeCode"].ToString(), expiresOn,
                Path.GetFileName(file.FileName), file.ContentType, buffer.ToArray()), ct);

            return result.IsSuccess
                ? Results.Created($"/api/v1/shops/{shopId}/compliance/documents/{result.Value}/file", new { id = result.Value })
                : Problem(result.Error!.Code, result.Error.Message, StatusFor(result.Error.Code));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(FileRules.MaxBytes + 1024 * 1024));

        api.MapGet("/shops/{shopId:guid}/compliance/documents", async (Guid shopId, IDispatcher d, CancellationToken ct) =>
            Results.Ok((await d.Ask(new ListComplianceDocuments(shopId), ct)).Value));

        api.MapGet("/shops/{shopId:guid}/compliance/documents/{id:guid}/file", async (Guid shopId, Guid id, HttpContext http, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new GetComplianceFile(shopId, id), ct);
            if (!result.IsSuccess) return Problem(result.Error!.Code, result.Error.Message, StatusFor(result.Error.Code));
            // The browser must treat the file as the type we stored and never guess, so an uploaded file cannot run as a page.
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers["Content-Security-Policy"] = "sandbox";
            return Results.File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
        });

        api.MapDelete("/shops/{shopId:guid}/compliance/documents/{id:guid}", async (Guid shopId, Guid id, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new DeleteComplianceDocument(shopId, deviceId, staffId, id), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result.Error!.Code, result.Error.Message, StatusFor(result.Error.Code));
        });
    }

    private static int StatusFor(string code) => code switch
    {
        "compliance.not_found" => 404,
        "compliance.file_unavailable" => 410,
        "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" or "permission.denied" => 403,
        "file.too_large" => 413,
        _ => 422,
    };

    private static IResult Problem(string code, string message, int status) =>
        Results.Problem(title: message, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
