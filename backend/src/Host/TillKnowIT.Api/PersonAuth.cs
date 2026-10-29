using System.Text;
using Identity.Application.Auth;
using TillKnowIT.Security;

namespace TillKnowIT.Api;

/// <summary>
/// Makes "which person is acting" mean something. If a request names an acting person (a callerPersonId, a personId...), that person must be the one who signed in:
/// the bearer token is checked and the ids must match, so nobody can act as someone else by typing their id. With Auth:Required on, a request that names a person
/// and has no valid token is refused. Requests that name no person (a till's own calls, public pages, webhooks) are not touched here.
/// </summary>
public sealed class PersonAuthMiddleware(RequestDelegate next, AuthSettings settings, IConfiguration config)
{
    private const int MaxBody = 1_000_000;
    private readonly bool _required = config.GetValue("Auth:Required", false);

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (HttpMethods.IsOptions(request.Method) || !request.Path.StartsWithSegments("/api/v1") || request.Path.StartsWithSegments("/api/v1/auth")) { await next(context); return; }

        var claims = new List<Guid>(PersonClaims.From(request.Query.Select(q => new KeyValuePair<string, string?>(q.Key, q.Value.ToString()))));
        claims.AddRange(PersonClaims.From(request.RouteValues.Select(r => new KeyValuePair<string, string?>(r.Key, r.Value?.ToString()))));
        if ((HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) || HttpMethods.IsPatch(request.Method)) && request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
            && request.ContentLength is null or <= MaxBody)
        {
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            claims.AddRange(PersonClaims.FromJson(await reader.ReadToEndAsync(context.RequestAborted)));
            request.Body.Position = 0;
        }

        var header = request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
        var subject = ExpiringToken.Verify(settings.TokenKey, token, DateTimeOffset.UtcNow);
        Guid? signedIn = Guid.TryParse(subject, out var id) ? id : null;

        if (claims.Count > 0)
        {
            if (signedIn is null && (_required || token is not null)) { await Refuse(context, 401, "auth.required", "Sign in to do this."); return; }
            if (signedIn is { } who && claims.Any(c => c != who)) { await Refuse(context, 403, "auth.person_mismatch", "You can only act as yourself."); return; }
        }
        await next(context);
    }

    private static Task Refuse(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return Results.Problem(title: message, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
    }
}
