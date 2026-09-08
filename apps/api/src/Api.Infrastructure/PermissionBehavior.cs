using System.Reflection;
using Api.Application;
using Microsoft.AspNetCore.Http;

namespace Api.Infrastructure;

public sealed class PermissionBehavior<TRequest, TResponse>(IHttpContextAccessor httpContextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var attribute = typeof(TRequest).GetCustomAttribute<RequiresPermissionAttribute>();

        if (attribute is null)
        {
            return await next();
        }

        var user = httpContextAccessor.HttpContext?.User;
        var hasPermission = user?.HasClaim("permission", attribute.Permission) == true;

        if (!hasPermission)
        {
            var error = new Error(
                "Permission.Denied", $"Missing required permission '{attribute.Permission}'.");
            return Api.Application.ResultReflectionHelper.CreateFailure<TResponse>(error);
        }

        return await next();
    }
}
