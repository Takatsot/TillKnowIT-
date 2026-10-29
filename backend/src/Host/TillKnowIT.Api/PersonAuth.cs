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
        if (HttpMethods.IsOptions(request.Method) || !request.Path.StartsWithSegments("/api/v1") || request.Path.StartsWithSegments("/api/v1/auth") || request.Path.Value!.EndsWith("/check-in", StringComparison.OrdinalIgnoreCase)) { await next(context); return; }

        var query = request.Query.Select(q => new KeyValuePair<string, string?>(q.Key, q.Value.ToString())).ToList();
        var route = request.RouteValues.Select(r => new KeyValuePair<string, string?>(r.Key, r.Value?.ToString())).ToList();
        var claims = new List<Guid>(PersonClaims.From(query));
        claims.AddRange(PersonClaims.From(route));
        var devices = new List<Guid>(PersonClaims.DevicesFrom(query));
        devices.AddRange(PersonClaims.DevicesFrom(route));
        if ((HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) || HttpMethods.IsPatch(request.Method)) && request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
            && request.ContentLength is null or <= MaxBody)
        {
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            claims.AddRange(PersonClaims.FromJson(body));
            devices.AddRange(PersonClaims.DevicesFromJson(body));
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

        // A till names its own device and no person: it must show the token it was given when it was set up. A signed-in person (the back office acting on a till) is
        // allowed through, because the person's own permissions are checked by the route.
        if (claims.Count == 0 && devices.Count > 0 && signedIn is null)
        {
            var deviceSubject = ExpiringToken.Verify(settings.TokenKey, request.Headers["X-Device-Token"].ToString(), DateTimeOffset.UtcNow);
            var proven = deviceSubject is not null && deviceSubject.StartsWith("device:", StringComparison.Ordinal) && Guid.TryParse(deviceSubject[7..].Split(':')[0], out var deviceId) && devices.All(d => d == deviceId);
            if (!proven && (_required || request.Headers.ContainsKey("X-Device-Token"))) { await Refuse(context, 401, "auth.device_required", "This till is not signed in. Set it up again."); return; }
        }
        await next(context);
    }

    private static Task Refuse(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return Results.Problem(title: message, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
    }
}
