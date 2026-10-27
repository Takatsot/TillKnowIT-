using Messaging.Application.Sending;
using Messaging.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.Application;

public static class MessagingApplicationExtensions
{
    /// <summary>Registers the queue other modules use and the default (placeholder) pricing.</summary>
    public static IServiceCollection AddMessagingApplication(this IServiceCollection services)
    {
        services.AddScoped<IMessageQueue, MessageQueue>();
        services.AddSingleton<IMessagePricing, DefaultMessagePricing>();
        services.AddSingleton<IDestinationChecker, DestinationChecker>();
        return services;
    }
}
