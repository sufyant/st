using Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace Api.Authorization;

// Authorization asks for permissions, never roles (0030). An endpoint names the permission it needs as its policy; the route
// groups add who may reach them at all.
internal static class AccessPolicies
{
    public const string TenantMember = "tenant-member";

    public const string SystemAdmin = "system-admin";

    public static IServiceCollection AddAccessAuthorization(this IServiceCollection services)
    {
        services.AddScoped<RequestAccess>();
        services.AddSingleton<IAuthorizationHandler, AccessHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AccessDeniedHandler>();

        return services.AddAuthorizationBuilder()
            .AddPolicy(TenantMember, policy => policy.RequireAuthenticatedUser().AddRequirements(new TenantMemberRequirement()))
            .AddPolicy(SystemAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ClerkAuthentication.SecondFactorClaim)
                .AddRequirements(new SystemAdminRequirement()))
            .Services;
    }

    private sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
    {
        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
            await base.GetPolicyAsync(policyName)
            ?? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName)).Build();
    }

    // A tenant route the caller cannot enter answers 404, so it looks the same as a tenant that does not exist (0015).
    private sealed class AccessDeniedHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();

        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Forbidden && TenantNotFound(context))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await _default.HandleAsync(next, context, policy, authorizeResult);
        }

        private static bool TenantNotFound(HttpContext context)
        {
            var metadata = context.GetEndpoint()?.Metadata;
            var access = context.RequestServices.GetRequiredService<RequestAccess>();

            return metadata?.GetMetadata<Tenants.TenantScopedEndpoint>() is not null && access.Membership is null;
        }
    }
}

internal sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

internal sealed record TenantMemberRequirement : IAuthorizationRequirement;

internal sealed record SystemAdminRequirement : IAuthorizationRequirement;

internal sealed class AccessHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (context.Resource is not HttpContext http)
        {
            return Task.CompletedTask;
        }

        var access = http.RequestServices.GetRequiredService<RequestAccess>();
        foreach (var requirement in context.PendingRequirements.ToList())
        {
            var met = requirement switch
            {
                PermissionRequirement permission => access.Has(permission.Permission),
                TenantMemberRequirement => access.Membership is not null,
                SystemAdminRequirement => access.SystemPermissions is not null,
                _ => false,
            };

            if (met)
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
