using Microsoft.Extensions.DependencyInjection;

namespace Api.Application;

// NOTE: Uses explicit MethodInfo.Invoke reflection rather than `dynamic` dispatch.
// The `dynamic` approach (typed in the task brief) fails at runtime with a
// RuntimeBinderException whenever the concrete request/handler/behavior types are
// not public to the call site (e.g. private nested test types): the DLR's runtime
// binder enforces normal C# accessibility rules from the call site's perspective,
// so it cannot see `Handle` on an inaccessible type even though the interface
// method is public. Invoking through the interface's MethodInfo sidesteps that,
// since reflection calls made via a public interface's method are always callable
// regardless of the implementing type's own visibility.
public sealed class Mediator(IServiceProvider serviceProvider) : IMediator
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();

        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handlerHandleMethod = handlerType.GetMethod("Handle")!;
        var handler = serviceProvider.GetRequiredService(handlerType);

        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviorHandleMethod = behaviorType.GetMethod("Handle")!;
        var behaviors = serviceProvider.GetServices(behaviorType).Reverse().ToList();

        RequestHandlerDelegate<TResponse> pipeline = () =>
            (Task<TResponse>)handlerHandleMethod.Invoke(handler, [request, cancellationToken])!;

        foreach (var behavior in behaviors)
        {
            var next = pipeline;
            var currentBehavior = behavior;
            pipeline = () =>
                (Task<TResponse>)behaviorHandleMethod.Invoke(currentBehavior, [request, next, cancellationToken])!;
        }

        return pipeline();
    }
}
