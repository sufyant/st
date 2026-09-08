using System.Reflection;
using System.Security.Claims;
using Api.Application;
using Microsoft.AspNetCore.Http;

namespace Api.Infrastructure;

/// Enforces both tenant-scope matching (for any <see cref="ITenantScopedRequest"/>)
/// and permission-string checking (for any request with <see cref="RequiresPermissionAttribute"/>).
public sealed class PermissionBehavior<TRequest, TResponse>(IHttpContextAccessor httpContextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (request is ITenantScopedRequest tenantScoped)
        {
            var tenantIdClaim = user?.FindFirstValue("tenant_id");
            var claimMatches = tenantIdClaim is not null
                && Guid.TryParse(tenantIdClaim, out var claimedTenantId)
                && claimedTenantId == tenantScoped.TenantId;

            if (!claimMatches)
            {
                var mismatchError = new Error(
                    "Tenant.Mismatch", "The request's tenant does not match the authenticated tenant context.");
                return Api.Application.ResultReflectionHelper.CreateFailure<TResponse>(mismatchError);
            }
        }

        var attribute = typeof(TRequest).GetCustomAttribute<RequiresPermissionAttribute>();

        if (attribute is null)
        {
            return await next();
        }

        var hasPermission = user?.HasClaim("permission", attribute.Permission) == true;

        if (!hasPermission)
        {
            var deniedError = new Error(
                "Permission.Denied", $"Missing required permission '{attribute.Permission}'.");
            return Api.Application.ResultReflectionHelper.CreateFailure<TResponse>(deniedError);
        }

        return await next();
    }
}
