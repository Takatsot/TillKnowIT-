using Accounting.Application;
using Accounting.Contracts;
using Accounting.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Security;

namespace Accounting.Infrastructure;

/// <summary>Everything the real Xero and Sage connections are built from. A package with no client id is simply not offered.</summary>
public sealed record AccountingOAuthOptions(string? TokenKey, string? ReturnUrl, XeroSettings? Xero, SageSettings? Sage);

/// <summary>What the browser carries to the package and back, signed so nobody can start a connection for someone else's shop.</summary>
public sealed record OAuthState(Guid ShopId, Guid PersonId, string Provider, DateOnly? StartsOn);

public sealed class OAuthStateSigner(string key, TimeProvider clock)
{
    private static readonly TimeSpan Life = TimeSpan.FromMinutes(15);

    public string Create(OAuthState state) =>
        ExpiringToken.Issue(key, $"oauth:{state.ShopId:N}:{state.PersonId:N}:{state.Provider}:{state.StartsOn?.ToString("yyyy-MM-dd") ?? "-"}", clock.GetUtcNow(), Life).Token;

    public OAuthState? Read(string? token)
    {
        var subject = ExpiringToken.Verify(key, token, clock.GetUtcNow());
        var parts = subject?.Split(':');
        if (parts is not { Length: 5 } || parts[0] != "oauth" || !Guid.TryParseExact(parts[1], "N", out var shop) || !Guid.TryParseExact(parts[2], "N", out var person)) return null;
        DateOnly? startsOn = DateOnly.TryParse(parts[4], out var day) ? day : null;
        return new(shop, person, parts[3], startsOn);
    }
}

public sealed record AccountingReturn(string? Url);

public static class AccountingOAuthExtensions
{
    /// <summary>
    /// Registers the real Xero and Sage connections for whichever have credentials (configuration Accounting:Xero:ClientId, ClientSecret, RedirectUri; the same for Sage), plus the encrypted token
    /// vault (Accounting:TokenKey, 32 bytes as base64). Call from the API (sign-in and connect) and the worker (the daily export). Does nothing without a token key.
    /// </summary>
    public static IServiceCollection AddAccountingOAuth(this IServiceCollection services, AccountingOAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TokenKey)) return services;
        var key = Convert.FromBase64String(options.TokenKey);
        if (key.Length != 32) throw new InvalidOperationException("Accounting:TokenKey must be 32 bytes as base64.");

        services.AddSingleton(new OAuthStateSigner(options.TokenKey, TimeProvider.System));
        services.AddSingleton(new AccountingReturn(options.ReturnUrl));
        services.AddScoped<IProviderTokenVault>(sp => new EncryptedProviderTokenVault(sp.GetRequiredService<AccountingDbContext>(), key, TimeProvider.System));
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (options.Xero is { } xero)
            services.AddScoped<IAccountingExporter>(sp => new XeroExporter(http, xero, sp.GetRequiredService<IProviderTokenVault>(), TimeProvider.System));
        if (options.Sage is { } sage)
            services.AddScoped<IAccountingExporter>(sp => new SageExporter(http, sage, sp.GetRequiredService<IProviderTokenVault>(), TimeProvider.System));
        return services;
    }

    /// <summary>Reads the options from configuration values (so the host does not need the configuration package here).</summary>
    public static AccountingOAuthOptions ReadOptions(Func<string, string?> get)
    {
        XeroSettings? xero = get("Accounting:Xero:ClientId") is { Length: > 0 } xid && get("Accounting:Xero:ClientSecret") is { Length: > 0 } xs && get("Accounting:Xero:RedirectUri") is { Length: > 0 } xr
            ? new XeroSettings(xid, xs, xr, get("Accounting:Xero:Scopes") is { Length: > 0 } scopes ? scopes : "offline_access accounting.manualjournals") : null;
        SageSettings? sage = get("Accounting:Sage:ClientId") is { Length: > 0 } sid && get("Accounting:Sage:ClientSecret") is { Length: > 0 } ss && get("Accounting:Sage:RedirectUri") is { Length: > 0 } sr
            ? new SageSettings(sid, ss, sr) : null;
        return new(get("Accounting:TokenKey"), get("Accounting:ReturnUrl"), xero, sage);
    }

    /// <summary>
    /// GET /shops/{id}/accounting/connections/{provider}/authorise: the owner's sign-in page for the package (the app sends the browser there).
    /// GET /accounting/oauth/callback: where the package sends the browser back; connects, then returns the browser to the back office.
    /// </summary>
    public static IEndpointRouteBuilder MapAccountingOAuthEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/shops/{shopId:guid}/accounting/connections/{provider}/authorise", async (Guid shopId, string provider, Guid personId, DateOnly? startsOn, IDispatcher d, IEnumerable<IAccountingExporter> exporters, OAuthStateSigner? signer, CancellationToken ct) =>
        {
            var check = await d.Ask(new CheckAccountingConnect(shopId, personId, provider), ct);
            if (!check.IsSuccess)
                return Results.Problem(title: check.Error!.Message, extensions: new Dictionary<string, object?> { ["code"] = check.Error.Code },
                    statusCode: check.Error.Code is "permission.denied" or "plan.feature_not_included" ? 403 : 422);
            var exporter = exporters.OfType<IOAuthExporter>().FirstOrDefault(e => e.Provider == check.Value!.Provider);
            if (exporter is null || signer is null)
                return Results.Problem(title: "Signing in to this package is not set up here yet.", extensions: new Dictionary<string, object?> { ["code"] = "accounting.oauth_unavailable" }, statusCode: 422);
            return Results.Ok(new AuthoriseResponse(exporter.AuthoriseUrl(signer.Create(new(shopId, personId, exporter.Provider, startsOn)))));
        });

        api.MapGet("/accounting/oauth/callback", async (string? code, string? state, string? error, IDispatcher d, OAuthStateSigner? signer, AccountingReturn? back, CancellationToken ct) =>
        {
            static IResult Done(string? url, string outcome, string? reason = null) => url is null
                ? (outcome == "connected" ? Results.Ok(new { outcome }) : Results.Problem(title: reason ?? "The connection failed.", statusCode: 422))
                : Results.Redirect($"{url}{(url.Contains('?') ? '&' : '?')}accounting={outcome}{(reason is null ? "" : "&reason=" + Uri.EscapeDataString(reason))}");

            var read = signer?.Read(state);
            if (read is null) return Done(back?.Url, "failed", "This sign-in link has expired. Start again.");
            if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code)) return Done(back?.Url, "failed", "The sign-in was cancelled.");
            var connected = await d.Send(new ConnectAccounting(read.ShopId, read.Provider, new ConnectRequest(read.PersonId, code, read.StartsOn)), ct);
            return connected.IsSuccess ? Done(back?.Url, "connected") : Done(back?.Url, "failed", connected.Error!.Message);
        });
        return api;
    }
}

public sealed record AuthoriseResponse(string Url);
