using System.Collections.Concurrent;
using System.Reflection;
using Application.Abstractions;
using Application.Results;

namespace Application.Behaviors;

public sealed class PermissionBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, string?> Required = new();

    public Task<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var permission = Required.GetOrAdd(
            typeof(TRequest),
            static type => type.GetCustomAttribute<RequiresPermissionAttribute>()?.Permission);

        if (permission is null || currentUser.HasPermission(permission))
        {
            return next();
        }

        return Task.FromResult<Result<TResponse>>(Error.Forbidden(
            "permission.missing",
            $"The permission '{permission}' is required."));
    }
}
