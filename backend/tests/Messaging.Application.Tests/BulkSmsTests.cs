using System.Net;
using System.Text;
using Messaging.Domain;
using Messaging.Infrastructure;

namespace Messaging.Application.Tests;

public class BulkSmsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return reply(request);
        }
    }

    private static OutboundMessage Sms(string body = "Your code is 123456") => OutboundMessage.Queue(MessageChannel.Sms, "0821234567", null, body, "otp", null, null, Now).Value!;

    private static BulkSmsProvider Provider(Stub stub, string? routing = null) =>
        new(new HttpClient(stub), new BulkSmsSettings("tok-id", "tok-secret", "https://api.bulksms.test/v1/", routing));

    [Fact]
    public async Task Sends_to_the_messages_endpoint_with_the_token_as_basic_auth_and_returns_the_providers_id()
    {
        var stub = new Stub(_ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""[{"id":"msg-77","type":"SENT","to":"+27821234567"}]""", Encoding.UTF8, "application/json") });

        var outcome = await Provider(stub, "ECONOMY").Send(Sms(), default);

        Assert.True(outcome.Success);
        Assert.Equal("msg-77", outcome.ProviderRef);
        var (request, body) = stub.Calls.Single();
        Assert.Equal("https://api.bulksms.test/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("tok-id:tok-secret")), request.Headers.Authorization!.ToString());
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var sent = json.RootElement;
        Assert.Equal("+27821234567", sent.GetProperty("to").GetString());
        Assert.Equal("Your code is 123456", sent.GetProperty("body").GetString());
        Assert.Equal("ECONOMY", sent.GetProperty("routingGroup").GetString());
        Assert.Equal("TEXT", sent.GetProperty("encoding").GetString());
    }

    [Fact]
    public async Task A_text_with_non_gsm_characters_goes_as_unicode()
    {
        var stub = new Stub(_ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("[]") });
        await Provider(stub).Send(Sms("Ngiyabonga – siyakubonga"), default);
        Assert.Contains("\"encoding\":\"UNICODE\"", stub.Calls.Single().Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task Only_answers_that_can_never_succeed_are_permanent(HttpStatusCode status, bool permanent)
    {
        var stub = new Stub(_ => new HttpResponseMessage(status) { Content = new StringContent("nope") });

        var outcome = await Provider(stub).Send(Sms(), default);

        Assert.False(outcome.Success);
        Assert.Equal(permanent, outcome.Permanent);
        Assert.Contains(((int)status).ToString(), outcome.Error);
    }

    [Fact]
    public async Task A_network_failure_is_retried_not_permanent()
    {
        var stub = new Stub(_ => throw new HttpRequestException("connection refused"));
        var outcome = await Provider(stub).Send(Sms(), default);
        Assert.False(outcome.Success);
        Assert.False(outcome.Permanent);
    }
}
