using System.Security.Claims;
using Tenancy;

namespace Api.Tenants;

// Turns away a tenant-scoped request whose tenant was not resolved: 401 without a user, otherwise 404, so a tenant the user
// cannot enter looks the same as one that does not exist (0015).
internal sealed class TenantRequirementFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (httpContext.RequestServices.GetRequiredService<TenantContext>().TenantId is not null)
        {
            return next(context);
        }

        var status = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) is null
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status404NotFound;

        return ValueTask.FromResult<object?>(TypedResults.Problem(statusCode: status));
    }
}
