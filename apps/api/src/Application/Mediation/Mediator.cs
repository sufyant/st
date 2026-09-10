using System.Collections.Concurrent;
using System.Reflection;
using Application.Abstractions;
using Application.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Mediation;

public sealed class Mediator(IServiceProvider provider) : IMediator
{
    private delegate Task<Result<TResponse>> Executor<TResponse>(
        IServiceProvider serviceProvider,
        object request,
        CancellationToken cancellationToken);

    // The call site knows only IRequest<TResponse>, so the path to the closed handler is built
    // once per request type by reflection and then reused as a delegate.
    private static readonly ConcurrentDictionary<Type, object> Executors = new();

    public Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();

        if (!Executors.TryGetValue(requestType, out var cached))
        {
            cached = CreateExecutor<TResponse>(requestType);
            Executors[requestType] = cached;
        }

        return ((Executor<TResponse>)cached)(provider, request, cancellationToken);
    }

    private static Executor<TResponse> CreateExecutor<TResponse>(Type requestType) =>
        typeof(Mediator)
            .GetMethod(nameof(ExecuteAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(requestType, typeof(TResponse))
            .CreateDelegate<Executor<TResponse>>();

    private static Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(
        IServiceProvider serviceProvider,
        object request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
                      ?? throw new InvalidOperationException(
                          $"No handler is registered for '{typeof(TRequest).Name}'.");
        var typed = (TRequest)request;
        RequestHandlerDelegate<TResponse> pipeline = () => handler.HandleAsync(typed, cancellationToken);

        // Registration order reads outside-in, so the chain is built from the innermost behavior out.
        foreach (var behavior in serviceProvider
                     .GetServices<IPipelineBehavior<TRequest, TResponse>>()
                     .Reverse())
        {
            var next = pipeline;
            pipeline = () => behavior.HandleAsync(typed, next, cancellationToken);
        }

        return pipeline();
    }
}
