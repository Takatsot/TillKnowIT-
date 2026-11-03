using Microsoft.AspNetCore.Diagnostics;

/// <summary>
/// A request body the server cannot read (not JSON, or an id that is not a Guid) is the caller's mistake, not ours: answer 400 with a code, rather than the 500 the
/// default handler gives, which makes a till retry the same bad upload for ever as if the server were down.
/// </summary>
internal sealed class BadRequestHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not BadHttpRequestException bad) return false;
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { type = "https://tools.ietf.org/html/rfc9110#section-15.5.1", title = "The request could not be read.", status = 400, code = "request.invalid", detail = bad.InnerException?.Message ?? bad.Message }, ct);
        return true;
    }
}
