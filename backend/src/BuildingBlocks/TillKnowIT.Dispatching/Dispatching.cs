using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TillKnowIT.SharedKernel;

namespace TillKnowIT.Dispatching;

public interface ICommand<TResult>;

public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> Handle(TQuery query, CancellationToken ct);
}

public interface IDispatcher
{
    Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default);
    Task<Result<TResult>> Ask<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    public Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default) =>
        Invoke<TResult>(typeof(ICommandHandler<,>), command, ct);

    public Task<Result<TResult>> Ask<TResult>(IQuery<TResult> query, CancellationToken ct = default) =>
        Invoke<TResult>(typeof(IQueryHandler<,>), query, ct);

    private Task<Result<TResult>> Invoke<TResult>(Type openHandler, object message, CancellationToken ct)
    {
        var handlerType = openHandler.MakeGenericType(message.GetType(), typeof(TResult));
        var handler = services.GetService(handlerType)
                      ?? throw new InvalidOperationException($"No handler registered for {message.GetType().Name}.");
        var handle = handlerType.GetMethod("Handle")!;
        return (Task<Result<TResult>>)handle.Invoke(handler, [message, ct])!;
    }
}

public static class DispatchingExtensions
{
    /// <summary>Registers the dispatcher and every command, query and event handler found in the given assemblies.</summary>
    public static IServiceCollection AddDispatching(this IServiceCollection services, params Assembly[] handlerAssemblies)
    {
        services.TryAddScoped<IDispatcher, Dispatcher>();

        var handlerTypes = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IDomainEventHandler<>) };
        foreach (var type in handlerAssemblies.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false }))
            foreach (var face in type.GetInterfaces().Where(i => i.IsGenericType && handlerTypes.Contains(i.GetGenericTypeDefinition())))
                services.AddScoped(face, type);

        return services;
    }
}
