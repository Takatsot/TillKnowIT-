using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Messaging.Application.Sending;
using Messaging.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.Infrastructure;

/// <summary>Where and as whom to send: from configuration (Sms:BulkSms:TokenId, TokenSecret, optionally BaseUrl and RoutingGroup).</summary>
public sealed record BulkSmsSettings(string TokenId, string TokenSecret, string BaseUrl = "https://api.bulksms.com/v1/", string? RoutingGroup = null);

/// <summary>
/// Sends texts through the BulkSMS REST API (POST /messages, HTTP Basic with an API token). A 4xx answer other than a rate limit means the text will never be accepted
/// (bad number, bad credentials, no credit) and is not retried; a network failure, a 5xx or a rate limit is retried by the queue.
/// </summary>
public sealed class BulkSmsProvider(HttpClient http, BulkSmsSettings settings) : IMessageChannelProvider
{
    public MessageChannel Channel => MessageChannel.Sms;

    public async Task<SendOutcome> Send(OutboundMessage message, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl), "messages"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.TokenId}:{settings.TokenSecret}")));
        // A text with any non-GSM character is sent as Unicode so it is not mangled; BulkSMS reads the encoding from this field.
        request.Content = JsonContent.Create(new Dictionary<string, object?>
        {
            ["to"] = message.Destination,
            ["body"] = message.Body,
            ["encoding"] = message.Body.All(c => c < 128) ? "TEXT" : "UNICODE",
            ["routingGroup"] = settings.RoutingGroup,
        }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new SendOutcome(false, Error: $"The SMS service could not be reached: {ex.Message}");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode) return new SendOutcome(true, ProviderRef: FirstId(text));
            var error = $"BulkSMS answered {(int)response.StatusCode}: {Trim(text)}";
            var permanent = (int)response.StatusCode is >= 400 and < 500 && response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout);
            return new SendOutcome(false, permanent, Error: error);
        }
    }

    private static string? FirstId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var first = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : doc.RootElement;
            return first.ValueKind == JsonValueKind.Object && first.TryGetProperty("id", out var id) ? id.ToString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Trim(string text) => text.Length <= 300 ? text : text[..300];
}

public static class BulkSmsExtensions
{
    /// <summary>
    /// Registers the BulkSMS provider when the API token id and secret are given (configuration Sms:BulkSms:TokenId and TokenSecret); otherwise does nothing, and SMS keeps
    /// failing visibly instead of pretending to send. Call from the worker.
    /// </summary>
    public static IServiceCollection AddBulkSmsIfConfigured(this IServiceCollection services, string? tokenId, string? tokenSecret, string? baseUrl = null, string? routingGroup = null)
    {
        if (string.IsNullOrWhiteSpace(tokenId) || string.IsNullOrWhiteSpace(tokenSecret)) return services;
        var settings = new BulkSmsSettings(tokenId, tokenSecret, string.IsNullOrWhiteSpace(baseUrl) ? "https://api.bulksms.com/v1/" : baseUrl, string.IsNullOrWhiteSpace(routingGroup) ? null : routingGroup);
        services.AddSingleton<IMessageChannelProvider>(new BulkSmsProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, settings));
        return services;
    }
}
