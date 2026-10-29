using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Accounting.Application;
using Accounting.Domain;
using Accounting.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace Accounting.Application.Tests;

public class OAuthExporterTests
{
    private static readonly DateOnly Day = new(2026, 10, 25);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.Zero));
    private readonly Vault _vault = new();

    private sealed class Vault : IProviderTokenVault
    {
        public Dictionary<string, OAuthTokens> Rows { get; } = [];
        public Task<string> Store(string provider, OAuthTokens tokens, CancellationToken ct) { var r = $"vault:{Rows.Count + 1}"; Rows[r] = tokens; return Task.FromResult(r); }
        public Task<OAuthTokens?> Load(string reference, CancellationToken ct) => Task.FromResult(Rows.TryGetValue(reference, out var t) ? t : null);
        public Task Replace(string reference, OAuthTokens tokens, CancellationToken ct) { Rows[reference] = tokens; return Task.CompletedTask; }
    }

    /// <summary>Answers by method and URL prefix; anything unlisted is a test mistake and says so.</summary>
    private sealed class Server : HttpMessageHandler
    {
        private readonly List<(string Method, string Prefix, Func<HttpRequestMessage, string, HttpResponseMessage> Reply)> _routes = [];
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        public Server On(string method, string prefix, Func<HttpRequestMessage, string, HttpResponseMessage> reply) { _routes.Add((method, prefix, reply)); return this; }
        public Server On(string method, string prefix, HttpStatusCode status, string json) => On(method, prefix, (_, _) => Json(status, json));

        public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            var route = _routes.FirstOrDefault(r => r.Method == request.Method.Method && request.RequestUri!.ToString().StartsWith(r.Prefix, StringComparison.Ordinal));
            return route.Reply is null ? Json(HttpStatusCode.NotFound, $"{{\"unexpected\":\"{request.Method} {request.RequestUri}\"}}") : route.Reply(request, body);
        }
    }

    private static Journal TwoLineJournal() => new(Day, [new("cash", "090", 11_500, 0, "Cash sales"), new("sales", "200", 0, 10_000, "Sales"), new("vat_output", "820", 0, 1_500, "VAT")]);

    private const string TokenJson = """{"access_token":"acc-1","refresh_token":"ref-1","expires_in":1800}""";

    // ---- Xero ----

    private XeroExporter Xero(Server server) => new(new HttpClient(server), new XeroSettings("xid", "xsecret", "https://api.test/api/v1/accounting/oauth/callback"), _vault, _clock);

    [Fact]
    public void Xero_sends_the_owner_to_its_sign_in_page_asking_for_manual_journals_only()
    {
        var url = Xero(new Server()).AuthoriseUrl("state-1");

        Assert.StartsWith("https://login.xero.com/identity/connect/authorize?", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=xid", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Fapi.test%2Fapi%2Fv1%2Faccounting%2Foauth%2Fcallback", url);
        Assert.Contains("scope=offline_access%20accounting.manualjournals", url);
        Assert.Contains("state=state-1", url);
    }

    [Fact]
    public async Task Xero_swaps_the_code_for_tokens_with_the_client_as_basic_auth_finds_the_organisation_and_keeps_tokens_only_in_the_vault()
    {
        var server = new Server()
            .On("POST", XeroExporter.TokenEndpoint, HttpStatusCode.OK, TokenJson)
            .On("GET", XeroExporter.ConnectionsEndpoint, HttpStatusCode.OK, """[{"tenantId":"t-1","tenantType":"PRACTICEMANAGER","tenantName":"Other"},{"tenantId":"t-2","tenantType":"ORGANISATION","tenantName":"Mama's Spaza"}]""");

        var result = await Xero(server).Exchange(" the-code ", default);

        Assert.Equal("Mama's Spaza", result.Value!.TenantName);
        var stored = _vault.Rows[result.Value.TokenReference];
        Assert.Equal(("acc-1", "ref-1", "t-2"), (stored.AccessToken, stored.RefreshToken, stored.TenantId));
        Assert.Equal(_clock.GetUtcNow().AddSeconds(1800), stored.ExpiresAt);
        var (token, body) = server.Calls[0];
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("xid:xsecret")), token.Headers.Authorization!.ToString());
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=the-code", body);
        Assert.DoesNotContain("acc-1", result.Value.TokenReference);
    }

    [Fact]
    public async Task Xero_posts_the_day_as_one_manual_journal_with_debits_positive_credits_negative_and_the_days_key_as_idempotency_key()
    {
        var reference = await Stored("t-9", expiresIn: TimeSpan.FromMinutes(20));
        var server = new Server().On("POST", XeroExporter.JournalsEndpoint, HttpStatusCode.OK, """{"ManualJournals":[{"ManualJournalID":"mj-42"}]}""");

        var result = await Xero(server).Post(reference, TwoLineJournal(), "tillknowit:abc:20261025", default);

        Assert.Equal("mj-42", result.Value);
        var (request, body) = server.Calls.Single();
        Assert.Equal("Bearer acc-old", request.Headers.Authorization!.ToString());
        Assert.Equal("t-9", request.Headers.GetValues("xero-tenant-id").Single());
        Assert.Equal("tillknowit:abc:20261025", request.Headers.GetValues("Idempotency-Key").Single());
        var journal = JsonNode.Parse(body)!["ManualJournals"]![0]!;
        Assert.Equal("2026-10-25", journal["Date"]!.GetValue<string>());
        Assert.Equal("POSTED", journal["Status"]!.GetValue<string>());
        var amounts = journal["JournalLines"]!.AsArray().Select(l => (l!["AccountCode"]!.GetValue<string>(), l["LineAmount"]!.GetValue<decimal>())).ToList();
        Assert.Equal([("090", 115.00m), ("200", -100.00m), ("820", -15.00m)], amounts);
        Assert.Equal(0m, amounts.Sum(a => a.Item2));
    }

    [Fact]
    public async Task Xero_refreshes_an_expiring_token_and_saves_the_new_pair_because_the_old_refresh_token_stops_working()
    {
        var reference = await Stored("t-9", expiresIn: TimeSpan.FromSeconds(20));
        var server = new Server()
            .On("POST", XeroExporter.TokenEndpoint, HttpStatusCode.OK, """{"access_token":"acc-2","refresh_token":"ref-2","expires_in":1800}""")
            .On("POST", XeroExporter.JournalsEndpoint, HttpStatusCode.OK, """{"ManualJournals":[{"ManualJournalID":"mj-1"}]}""");

        var result = await Xero(server).Post(reference, TwoLineJournal(), "k", default);

        Assert.True(result.IsSuccess);
        Assert.Contains("grant_type=refresh_token", server.Calls[0].Body);
        Assert.Contains("refresh_token=ref-old", server.Calls[0].Body);
        Assert.Equal(("acc-2", "ref-2", "t-9"), (_vault.Rows[reference].AccessToken, _vault.Rows[reference].RefreshToken, _vault.Rows[reference].TenantId));
        Assert.Equal("Bearer acc-2", server.Calls[1].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Xero_failures_come_back_as_plain_results_never_exceptions()
    {
        var reference = await Stored("t-9", expiresIn: TimeSpan.FromMinutes(20));
        var refused = new Server().On("POST", XeroExporter.JournalsEndpoint, HttpStatusCode.BadRequest, """{"Message":"Account code 090 is not valid"}""");
        var failed = await Xero(refused).Post(reference, TwoLineJournal(), "k", default);
        Assert.Equal("accounting.post_failed", failed.Error!.Code);
        Assert.Contains("400", failed.Error.Message);
        Assert.Contains("090", failed.Error.Message);

        var down = new Server().On("POST", XeroExporter.JournalsEndpoint, (_, _) => throw new HttpRequestException("no network"));
        Assert.Equal("accounting.unreachable", (await Xero(down).Post(reference, TwoLineJournal(), "k", default)).Error!.Code);

        Assert.Equal("accounting.token_missing", (await Xero(new Server()).Post("vault:none", TwoLineJournal(), "k", default)).Error!.Code);

        var badRefresh = await Stored("t-9", expiresIn: TimeSpan.Zero);
        var cancelled = new Server().On("POST", XeroExporter.TokenEndpoint, HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        Assert.Equal("accounting.connect_failed", (await Xero(cancelled).Post(badRefresh, TwoLineJournal(), "k", default)).Error!.Code);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_without_keeping_anything()
    {
        var server = new Server().On("POST", XeroExporter.TokenEndpoint, HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");

        var result = await Xero(server).Exchange("bad", default);

        Assert.Equal("accounting.connect_failed", result.Error!.Code);
        Assert.Empty(_vault.Rows);
        Assert.Equal("accounting.code_invalid", (await Xero(server).Exchange("  ", default)).Error!.Code);
    }

    // ---- Sage ----

    private SageExporter Sage(Server server) => new(new HttpClient(server), new SageSettings("sid", "ssecret", "https://api.test/api/v1/accounting/oauth/callback"), _vault, _clock);

    private const string Api = "https://api.accounting.sage.com/v3.1/";

    private const string Ledgers = """{"$items":[{"id":"L-cash","nominal_code":"090"},{"id":"L-sales","nominal_code":"200"},{"id":"L-vat","nominal_code":"820"}]}""";

    [Fact]
    public void Sage_sends_the_owner_to_its_central_sign_in_page_for_the_v3_1_api()
    {
        var url = Sage(new Server()).AuthoriseUrl("s-2");

        Assert.StartsWith("https://www.sageone.com/oauth2/auth/central?filter=apiv3.1&", url);
        Assert.Contains("client_id=sid", url);
        Assert.Contains("scope=full_access", url);
        Assert.Contains("state=s-2", url);
    }

    [Fact]
    public async Task Sage_swaps_the_code_in_the_form_body_and_keeps_the_business_it_will_post_to()
    {
        var server = new Server()
            .On("POST", SageExporter.TokenEndpoint, HttpStatusCode.OK, TokenJson)
            .On("GET", Api + "businesses", HttpStatusCode.OK, """{"$items":[{"id":"biz-7","name":"Mama's Spaza (Pty) Ltd"}]}""");

        var result = await Sage(server).Exchange("sage-code", default);

        Assert.Equal("Mama's Spaza (Pty) Ltd", result.Value!.TenantName);
        Assert.Equal("biz-7", _vault.Rows[result.Value.TokenReference].TenantId);
        var body = server.Calls[0].Body;
        Assert.Contains("client_id=sid", body);
        Assert.Contains("client_secret=ssecret", body);
        Assert.Contains("code=sage-code", body);
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Null(server.Calls[0].Request.Headers.Authorization);
    }

    [Fact]
    public async Task Sage_looks_up_ledger_accounts_by_nominal_code_and_posts_one_journal_with_a_stable_reference()
    {
        var reference = await Stored("biz-7", expiresIn: TimeSpan.FromMinutes(4));
        var server = new Server()
            .On("GET", Api + "journals?", HttpStatusCode.OK, """{"$items":[{"id":"j-other","reference":"SOMETHING-ELSE"}]}""")
            .On("GET", Api + "ledger_accounts?", HttpStatusCode.OK, Ledgers)
            .On("POST", Api + "journals", HttpStatusCode.Created, """{"id":"j-55"}""");

        var result = await Sage(server).Post(reference, TwoLineJournal(), "tillknowit:abc:20261025", default);

        Assert.Equal("j-55", result.Value);
        var post = server.Calls.Last();
        Assert.Equal("biz-7", post.Request.Headers.GetValues("X-Business").Single());
        var journal = JsonNode.Parse(post.Body)!["journal"]!;
        Assert.Equal("2026-10-25", journal["date"]!.GetValue<string>());
        Assert.Equal(SageExporter.ReferenceFor("tillknowit:abc:20261025"), journal["reference"]!.GetValue<string>());
        var lines = journal["journal_lines"]!.AsArray().Select(l => (l!["ledger_account_id"]!.GetValue<string>(), l["debit"]!.GetValue<decimal>(), l["credit"]!.GetValue<decimal>())).ToList();
        Assert.Equal([("L-cash", 115.00m, 0m), ("L-sales", 0m, 100.00m), ("L-vat", 0m, 15.00m)], lines);
        Assert.Equal(SageExporter.ReferenceFor("a"), SageExporter.ReferenceFor("a"));
        Assert.NotEqual(SageExporter.ReferenceFor("a"), SageExporter.ReferenceFor("b"));
    }

    [Fact]
    public async Task Sage_does_not_post_a_day_twice_it_returns_the_journal_it_already_has()
    {
        var reference = await Stored("biz-7", expiresIn: TimeSpan.FromMinutes(4));
        var ours = SageExporter.ReferenceFor("tillknowit:abc:20261025");
        var server = new Server().On("GET", Api + "journals?", HttpStatusCode.OK, $$"""{"$items":[{"id":"j-already","reference":"{{ours}}"}]}""");

        var result = await Sage(server).Post(reference, TwoLineJournal(), "tillknowit:abc:20261025", default);

        Assert.Equal("j-already", result.Value);
        Assert.DoesNotContain(server.Calls, c => c.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Sage_names_the_mapping_to_fix_when_a_code_is_not_a_ledger_account()
    {
        var reference = await Stored("biz-7", expiresIn: TimeSpan.FromMinutes(4));
        var server = new Server()
            .On("GET", Api + "journals?", HttpStatusCode.OK, """{"$items":[]}""")
            .On("GET", Api + "ledger_accounts?", HttpStatusCode.OK, """{"$items":[{"id":"L-cash","nominal_code":"090"}]}""");

        var result = await Sage(server).Post(reference, TwoLineJournal(), "k", default);

        Assert.Equal("accounting.account_unknown", result.Error!.Code);
        Assert.Contains("200", result.Error.Message);
        Assert.Contains("sales", result.Error.Message);
    }

    [Fact]
    public async Task Sage_follows_the_next_page_of_ledger_accounts_and_refreshes_its_short_lived_token()
    {
        var reference = await Stored("biz-7", expiresIn: TimeSpan.FromSeconds(10));
        var server = new Server()
            .On("POST", SageExporter.TokenEndpoint, HttpStatusCode.OK, """{"access_token":"acc-9","refresh_token":"ref-9","expires_in":300}""")
            .On("GET", Api + "journals?", HttpStatusCode.OK, """{"$items":[]}""")
            .On("GET", Api + "ledger_accounts?visible_in=journals&items_per_page=200&page=2", HttpStatusCode.OK, """{"$items":[{"id":"L-sales","nominal_code":"200"},{"id":"L-vat","nominal_code":"820"}]}""")
            .On("GET", Api + "ledger_accounts?", HttpStatusCode.OK, """{"$items":[{"id":"L-cash","nominal_code":"090"}],"$next":"/v3.1/ledger_accounts?visible_in=journals&items_per_page=200&page=2"}""")
            .On("POST", Api + "journals", HttpStatusCode.Created, """{"id":"j-1"}""");

        var result = await Sage(server).Post(reference, TwoLineJournal(), "k", default);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("ref-9", _vault.Rows[reference].RefreshToken);
        Assert.All(server.Calls.Where(c => c.Request.Method == HttpMethod.Get), c => Assert.Equal("Bearer acc-9", c.Request.Headers.Authorization!.ToString()));
    }

    // ---- the token vault's cipher, the sign-in state, and who may connect ----

    [Fact]
    public void Tokens_are_encrypted_and_only_come_back_under_the_same_key()
    {
        var key = new byte[32]; Random.Shared.NextBytes(key);
        var other = new byte[32]; Random.Shared.NextBytes(other);
        var tokens = new OAuthTokens("access-secret", "refresh-secret", _clock.GetUtcNow(), "t-1");

        var blob = TokenCipher.Encrypt(key, tokens);

        Assert.DoesNotContain("access-secret", Encoding.UTF8.GetString(blob));
        Assert.Equal(tokens, TokenCipher.Decrypt(key, blob));
        Assert.Null(TokenCipher.Decrypt(other, blob));
        blob[^1] ^= 0xFF;
        Assert.Null(TokenCipher.Decrypt(key, blob));
        Assert.Null(TokenCipher.Decrypt(key, [1, 2, 3]));
        Assert.NotEqual(TokenCipher.Encrypt(key, tokens), TokenCipher.Encrypt(key, tokens));   // a new nonce every time
    }

    [Fact]
    public void The_sign_in_state_names_the_shop_and_person_expires_and_cannot_be_forged()
    {
        var signer = new OAuthStateSigner("test-key-test-key-test-key", _clock);
        var state = new OAuthState(Guid.NewGuid(), Guid.NewGuid(), "xero", new DateOnly(2026, 11, 1));

        var token = signer.Create(state);

        Assert.Equal(state, signer.Read(token));
        Assert.Equal(state with { StartsOn = null }, signer.Read(signer.Create(state with { StartsOn = null })));
        Assert.Null(new OAuthStateSigner("another-key-another-key-another", _clock).Read(token));
        Assert.Null(signer.Read(token + "x"));
        Assert.Null(signer.Read(null));
        _clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Null(signer.Read(token));
    }

    [Fact]
    public void Options_are_read_from_configuration_and_a_package_without_credentials_is_left_out()
    {
        var values = new Dictionary<string, string?>
        {
            ["Accounting:TokenKey"] = "k", ["Accounting:ReturnUrl"] = "https://office.test/",
            ["Accounting:Xero:ClientId"] = "x", ["Accounting:Xero:ClientSecret"] = "s", ["Accounting:Xero:RedirectUri"] = "https://r",
            ["Accounting:Sage:ClientId"] = "y",
        };

        var options = AccountingOAuthExtensions.ReadOptions(k => values.GetValueOrDefault(k));

        Assert.Equal("https://r", options.Xero!.RedirectUri);
        Assert.Equal("offline_access accounting.manualjournals", options.Xero.Scopes);
        Assert.Null(options.Sage);
        Assert.Equal("https://office.test/", options.ReturnUrl);
    }

    // ---- helpers ----

    private async Task<string> Stored(string tenant, TimeSpan expiresIn) =>
        await _vault.Store("p", new OAuthTokens("acc-old", "ref-old", _clock.GetUtcNow() + expiresIn, tenant), default);
}
