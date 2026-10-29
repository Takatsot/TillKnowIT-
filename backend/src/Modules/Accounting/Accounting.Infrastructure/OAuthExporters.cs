using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Accounting.Application;
using Accounting.Domain;
using TillKnowIT.SharedKernel;

namespace Accounting.Infrastructure;

/// <summary>Shared by the packages that sign in with OAuth: keeps the access token fresh (the packages rotate refresh tokens, so the new set is saved each time) and never throws at the caller.</summary>
public abstract class OAuthExporterBase(string provider, HttpClient http, IProviderTokenVault vault, TimeProvider clock) : IOAuthExporter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);

    protected HttpClient Http => http;
    protected TimeProvider Clock => clock;

    public string Provider => provider;
    public abstract string AuthoriseUrl(string state);

    protected abstract Task<Result<OAuthTokens>> ExchangeCode(string code, CancellationToken ct);
    protected abstract Task<Result<OAuthTokens>> Refresh(OAuthTokens old, CancellationToken ct);
    protected abstract Task<Result<Tenant>> FindTenant(string accessToken, CancellationToken ct);
    protected abstract Task<Result<string>> PostJournal(OAuthTokens tokens, Journal journal, string idempotencyKey, CancellationToken ct);

    protected sealed record Tenant(string Id, string? Name);

    private static bool Transient(Exception ex, CancellationToken ct) => !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException;

    public async Task<Result<ExchangeResult>> Exchange(string authorisationCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorisationCode)) return Result<ExchangeResult>.Fail("accounting.code_invalid", "No code.");
        try
        {
            var tokens = await ExchangeCode(authorisationCode.Trim(), ct);
            if (!tokens.IsSuccess) return Result<ExchangeResult>.Fail(tokens.Error!.Code, tokens.Error.Message);
            var tenant = await FindTenant(tokens.Value!.AccessToken, ct);
            if (!tenant.IsSuccess) return Result<ExchangeResult>.Fail(tenant.Error!.Code, tenant.Error.Message);
            var reference = await vault.Store(provider, tokens.Value with { TenantId = tenant.Value!.Id }, ct);
            return Result<ExchangeResult>.Ok(new(reference, tenant.Value.Name));
        }
        catch (Exception ex) when (Transient(ex, ct))
        {
            return Result<ExchangeResult>.Fail("accounting.unreachable", $"{provider} could not be reached.");
        }
    }

    public async Task<Result<string>> Post(string tokenReference, Journal journal, string idempotencyKey, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(tokenReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);   // one refresh at a time per connection: a second would use a refresh token the first just cancelled
        try
        {
            var tokens = await vault.Load(tokenReference, ct);
            if (tokens is null) return Result<string>.Fail("accounting.token_missing", $"The {provider} sign-in was lost. Connect {provider} again.");
            if (tokens.ExpiresAt - clock.GetUtcNow() < Margin)
            {
                var fresh = await Refresh(tokens, ct);
                if (!fresh.IsSuccess) return Result<string>.Fail(fresh.Error!.Code, fresh.Error.Message);
                tokens = fresh.Value! with { TenantId = tokens.TenantId };
                await vault.Replace(tokenReference, tokens, ct);
            }
            return await PostJournal(tokens, journal, idempotencyKey, ct);
        }
        catch (Exception ex) when (Transient(ex, ct))
        {
            return Result<string>.Fail("accounting.unreachable", $"{provider} could not be reached: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    protected static decimal Money(long cents) => cents / 100m;

    protected static string Form(params (string Key, string Value)[] fields) => string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));

    protected static HttpRequestMessage Bearer(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>Posts a form to a token endpoint and reads access_token, refresh_token and expires_in from the answer.</summary>
    protected async Task<Result<OAuthTokens>> TokenRequest(string url, string body, string? basicUser, string? basicSecret, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded") };
        if (basicUser is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basicUser}:{basicSecret}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return Result<OAuthTokens>.Fail("accounting.connect_failed", $"{provider} refused the sign-in ({(int)response.StatusCode}). Connect {provider} again.");
        var json = JsonNode.Parse(text)?.AsObject();
        var access = json?["access_token"]?.GetValue<string>();
        var refresh = json?["refresh_token"]?.GetValue<string>();
        if (access is null || refresh is null) return Result<OAuthTokens>.Fail("accounting.connect_failed", $"{provider} did not give the access we need.");
        var seconds = json!["expires_in"]?.GetValue<int>() ?? 300;
        return Result<OAuthTokens>.Ok(new(access, refresh, clock.GetUtcNow().AddSeconds(seconds), ""));
    }

    protected static string Trim(string text) => text.Length <= 300 ? text : text[..300];
}

// ---------------------------------------------------------------- Xero

public sealed record XeroSettings(string ClientId, string ClientSecret, string RedirectUri, string Scopes = "offline_access accounting.manualjournals");

/// <summary>
/// Xero: OAuth 2.0 authorisation code. A day's journal becomes one manual journal (debits positive, credits negative), posted with the day's key as Idempotency-Key so
/// a repeat cannot post twice. Written to Xero's documentation; it has not been run against a Xero organisation yet.
/// </summary>
public sealed class XeroExporter(HttpClient http, XeroSettings settings, IProviderTokenVault vault, TimeProvider clock) : OAuthExporterBase(Providers.Xero, http, vault, clock)
{
    public const string AuthoriseEndpoint = "https://login.xero.com/identity/connect/authorize";
    public const string TokenEndpoint = "https://identity.xero.com/connect/token";
    public const string ConnectionsEndpoint = "https://api.xero.com/connections";
    public const string JournalsEndpoint = "https://api.xero.com/api.xro/2.0/ManualJournals";

    public override string AuthoriseUrl(string state) =>
        $"{AuthoriseEndpoint}?{Form(("response_type", "code"), ("client_id", settings.ClientId), ("redirect_uri", settings.RedirectUri), ("scope", settings.Scopes), ("state", state))}";

    protected override Task<Result<OAuthTokens>> ExchangeCode(string code, CancellationToken ct) =>
        TokenRequest(TokenEndpoint, Form(("grant_type", "authorization_code"), ("code", code), ("redirect_uri", settings.RedirectUri)), settings.ClientId, settings.ClientSecret, ct);

    protected override Task<Result<OAuthTokens>> Refresh(OAuthTokens old, CancellationToken ct) =>
        TokenRequest(TokenEndpoint, Form(("grant_type", "refresh_token"), ("refresh_token", old.RefreshToken)), settings.ClientId, settings.ClientSecret, ct);

    protected override async Task<Result<Tenant>> FindTenant(string accessToken, CancellationToken ct)
    {
        using var request = Bearer(HttpMethod.Get, ConnectionsEndpoint, accessToken);
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return Result<Tenant>.Fail("accounting.connect_failed", "Xero did not say which organisation was connected.");
        var list = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?.AsArray();
        var chosen = list?.FirstOrDefault(c => string.Equals(c?["tenantType"]?.GetValue<string>(), "ORGANISATION", StringComparison.OrdinalIgnoreCase)) ?? list?.FirstOrDefault();
        var id = chosen?["tenantId"]?.GetValue<string>();
        return id is null ? Result<Tenant>.Fail("accounting.connect_failed", "No Xero organisation was connected. Choose one when you sign in.") : Result<Tenant>.Ok(new(id, chosen?["tenantName"]?.GetValue<string>()));
    }

    protected override async Task<Result<string>> PostJournal(OAuthTokens tokens, Journal journal, string idempotencyKey, CancellationToken ct)
    {
        var lines = new JsonArray();
        foreach (var l in journal.Lines)
            lines.Add(new JsonObject { ["LineAmount"] = Money(l.DebitCents - l.CreditCents), ["AccountCode"] = l.AccountCode, ["Description"] = l.Narration });
        var body = new JsonObject
        {
            ["ManualJournals"] = new JsonArray(new JsonObject
            {
                ["Narration"] = $"TillKnowIT takings {journal.Day:yyyy-MM-dd}",
                ["Date"] = journal.Day.ToString("yyyy-MM-dd"),
                ["Status"] = "POSTED",
                ["LineAmountTypes"] = "NoTax",
                ["JournalLines"] = lines,
            }),
        };
        using var request = Bearer(HttpMethod.Post, JournalsEndpoint, tokens.AccessToken);
        request.Headers.Add("xero-tenant-id", tokens.TenantId);
        request.Headers.Add("Idempotency-Key", idempotencyKey.Length <= 128 ? idempotencyKey : idempotencyKey[..128]);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return Result<string>.Fail("accounting.post_failed", $"Xero answered {(int)response.StatusCode}: {Trim(text)}");
        var id = JsonNode.Parse(text)?["ManualJournals"]?.AsArray().FirstOrDefault()?["ManualJournalID"]?.GetValue<string>();
        return id is null ? Result<string>.Fail("accounting.post_failed", "Xero did not return a journal id.") : Result<string>.Ok(id);
    }
}

// ---------------------------------------------------------------- Sage

public sealed record SageSettings(string ClientId, string ClientSecret, string RedirectUri, string Scope = "full_access", string ApiBase = "https://api.accounting.sage.com/v3.1/");

/// <summary>
/// Sage Business Cloud Accounting (v3.1): OAuth 2.0 authorisation code. Sage has no idempotency header, so before posting it looks for a journal with our reference on that
/// day and returns it instead of posting again. Journal lines need Sage's own ledger account ids, found from the account codes the owner mapped (the ledger account's nominal code).
/// Written to Sage's documentation; it has not been run against a Sage business yet.
/// </summary>
public sealed class SageExporter(HttpClient http, SageSettings settings, IProviderTokenVault vault, TimeProvider clock) : OAuthExporterBase(Providers.Sage, http, vault, clock)
{
    public const string AuthoriseEndpoint = "https://www.sageone.com/oauth2/auth/central?filter=apiv3.1";
    public const string TokenEndpoint = "https://oauth.accounting.sage.com/token";

    private Uri Api(string path) => new(new Uri(settings.ApiBase), path);

    public override string AuthoriseUrl(string state) =>
        $"{AuthoriseEndpoint}&{Form(("response_type", "code"), ("client_id", settings.ClientId), ("redirect_uri", settings.RedirectUri), ("scope", settings.Scope), ("state", state))}";

    protected override Task<Result<OAuthTokens>> ExchangeCode(string code, CancellationToken ct) =>
        TokenRequest(TokenEndpoint, Form(("client_id", settings.ClientId), ("client_secret", settings.ClientSecret), ("code", code), ("grant_type", "authorization_code"), ("redirect_uri", settings.RedirectUri)), null, null, ct);

    protected override Task<Result<OAuthTokens>> Refresh(OAuthTokens old, CancellationToken ct) =>
        TokenRequest(TokenEndpoint, Form(("client_id", settings.ClientId), ("client_secret", settings.ClientSecret), ("grant_type", "refresh_token"), ("refresh_token", old.RefreshToken)), null, null, ct);

    protected override async Task<Result<Tenant>> FindTenant(string accessToken, CancellationToken ct)
    {
        using var request = Bearer(HttpMethod.Get, Api("businesses").ToString(), accessToken);
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return Result<Tenant>.Fail("accounting.connect_failed", "Sage did not say which business was connected.");
        var first = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["$items"]?.AsArray().FirstOrDefault();
        var id = first?["id"]?.GetValue<string>();
        return id is null ? Result<Tenant>.Fail("accounting.connect_failed", "No Sage business was connected.") : Result<Tenant>.Ok(new(id, first?["name"]?.GetValue<string>()));
    }

    private HttpRequestMessage Call(HttpMethod method, Uri url, OAuthTokens tokens)
    {
        var request = Bearer(method, url.ToString(), tokens.AccessToken);
        if (!string.IsNullOrEmpty(tokens.TenantId)) request.Headers.Add("X-Business", tokens.TenantId);
        return request;
    }

    /// <summary>Every page of a Sage list, following $next, up to a sane limit.</summary>
    private async Task<Result<List<JsonNode>>> Items(Uri first, OAuthTokens tokens, CancellationToken ct)
    {
        var items = new List<JsonNode>();
        var url = first;
        for (var page = 0; page < 10; page++)
        {
            using var request = Call(HttpMethod.Get, url, tokens);
            using var response = await Http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) return Result<List<JsonNode>>.Fail("accounting.post_failed", $"Sage answered {(int)response.StatusCode}: {Trim(text)}");
            var json = JsonNode.Parse(text);
            foreach (var item in json?["$items"]?.AsArray() ?? []) if (item is not null) items.Add(item);
            var next = json?["$next"]?.GetValue<string>();
            if (string.IsNullOrEmpty(next)) break;
            url = new Uri(url, next);
        }
        return Result<List<JsonNode>>.Ok(items);
    }

    /// <summary>Sage's reference for a day: ours is long, so a short stable one is made from it.</summary>
    public static string ReferenceFor(string idempotencyKey) => "TK-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))[..12];

    protected override async Task<Result<string>> PostJournal(OAuthTokens tokens, Journal journal, string idempotencyKey, CancellationToken ct)
    {
        var reference = ReferenceFor(idempotencyKey);
        var day = journal.Day.ToString("yyyy-MM-dd");

        var existing = await Items(Api($"journals?from_date={day}&to_date={day}&items_per_page=200"), tokens, ct);
        if (!existing.IsSuccess) return Result<string>.Fail(existing.Error!.Code, existing.Error.Message);
        var already = existing.Value!.FirstOrDefault(j => j["reference"]?.GetValue<string>() == reference)?["id"]?.GetValue<string>();
        if (already is not null) return Result<string>.Ok(already);

        var accounts = await Items(Api("ledger_accounts?visible_in=journals&items_per_page=200"), tokens, ct);
        if (!accounts.IsSuccess) return Result<string>.Fail(accounts.Error!.Code, accounts.Error.Message);
        var ids = new Dictionary<string, string>();
        foreach (var a in accounts.Value!)
            if (a["nominal_code"]?.ToString() is { } code && a["id"]?.GetValue<string>() is { } id) ids[code] = id;

        var lines = new JsonArray();
        foreach (var l in journal.Lines)
        {
            if (!ids.TryGetValue(l.AccountCode, out var ledgerId)) return Result<string>.Fail("accounting.account_unknown", $"Sage has no account with code {l.AccountCode} that can be used in journals. Fix the mapping for {l.AccountKey}.");
            lines.Add(new JsonObject { ["ledger_account_id"] = ledgerId, ["debit"] = Money(l.DebitCents), ["credit"] = Money(l.CreditCents), ["details"] = l.Narration });
        }
        var body = new JsonObject { ["journal"] = new JsonObject { ["date"] = day, ["reference"] = reference, ["description"] = $"TillKnowIT takings {day}", ["journal_lines"] = lines } };
        using var request = Call(HttpMethod.Post, Api("journals"), tokens);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return Result<string>.Fail("accounting.post_failed", $"Sage answered {(int)response.StatusCode}: {Trim(text)}");
        var created = JsonNode.Parse(text)?["id"]?.GetValue<string>();
        return created is null ? Result<string>.Fail("accounting.post_failed", "Sage did not return a journal id.") : Result<string>.Ok(created);
    }
}
