using Messaging.Application.Sending;
using Messaging.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TillKnowIT.Dispatching;

namespace Messaging.Infrastructure;

/// <summary>Delivers queued messages in the worker. Idle for a few seconds when there is nothing to send.</summary>
public sealed class MessageSender(IServiceScopeFactory scopes, ILogger<MessageSender> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var busy = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new SendDueMessages(), stoppingToken);
                if (result.IsSuccess && result.Value is { } r)
                {
                    busy = r.Sent + r.Retrying + r.Failed > 0;
                    if (busy) log.LogInformation("Messages: {Sent} sent, {Retrying} to retry, {Failed} failed", r.Sent, r.Retrying, r.Failed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Sending queued messages failed");
            }

            if (!busy) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

/// <summary>
/// Stand-in providers that only write to the log. They are registered in Development and never otherwise: in
/// staging and production a channel with no real provider fails permanently and visibly instead of pretending to send.
/// </summary>
internal sealed class LoggingChannelProvider(MessageChannel channel, ILogger<LoggingChannelProvider> log) : IMessageChannelProvider
{
    public MessageChannel Channel => channel;

    public Task<SendOutcome> Send(OutboundMessage message, CancellationToken ct)
    {
        log.LogInformation("DEV {Channel} to {Destination}: {Subject} {Body}", message.Channel, message.Destination, message.Subject, message.Body);
        return Task.FromResult(new SendOutcome(true, ProviderRef: $"dev-{message.Id:N}"));
    }
}

public static class MessagingHostingExtensions
{
    /// <summary>Runs the sender. Call this from the worker only.</summary>
    public static IServiceCollection AddMessageSender(this IServiceCollection services) => services.AddHostedService<MessageSender>();

    /// <summary>Development only: pretend providers that log instead of sending.</summary>
    public static IServiceCollection AddMessagingDevProviders(this IServiceCollection services)
    {
        foreach (var channel in Enum.GetValues<MessageChannel>())
            services.AddSingleton<IMessageChannelProvider>(sp => new LoggingChannelProvider(channel, sp.GetRequiredService<ILogger<LoggingChannelProvider>>()));
        return services;
    }
}
