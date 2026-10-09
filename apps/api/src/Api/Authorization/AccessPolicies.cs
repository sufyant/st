using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace Api.Authorization;

// Authorization asks for permissions, never roles. Every endpoint states its access (A5): public with AllowAnonymous, signed-in
// only with RequireSignedIn, or the permission it needs as its policy. The route groups add who may reach them at all.
internal static class AccessPolicies
{
    public const string TenantMember = "tenant-member";

    public const string SystemAdmin = "system-admin";

    public const string SignedIn = "signed-in";

    public static IServiceCollection AddAccessAuthorization(this IServiceCollection services)
    {
        services.AddScoped<RequestAccess>();
        services.AddSingleton<IAuthorizationHandler, AccessHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AccessDeniedHandler>();

        return services.AddAuthorizationBuilder()
            .AddPolicy(TenantMember, policy => policy.RequireAuthenticatedUser().AddRequirements(new TenantMemberRequirement()))
            .AddPolicy(SystemAdmin, policy => policy.RequireAuthenticatedUser().AddRequirements(new SystemAdminRequirement()))
            .AddPolicy(SignedIn, policy => policy.RequireAuthenticatedUser())

            // An endpoint that states no access is refused, never opened.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().AddRequirements(new AccessStatedRequirement()).Build())
            .Services;
    }

    /// <summary>The signed-in only state (A5): any signed-in user may call the endpoint, without a permission.</summary>
    public static TBuilder RequireSignedIn<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(SignedIn).WithMetadata(new SignedInEndpoint());

    private sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
    {
        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
            await base.GetPolicyAsync(policyName)
            ?? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName)).Build();
    }

    // A tenant route the caller cannot enter answers 404, so it looks the same as a tenant that does not exist.
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

// The fallback policy's only requirement. It reaches a request whose endpoint states no access; a request that matched no route
// (an unknown path, or a method the route does not take) has nothing to open and keeps its 404 or 405.
internal sealed record AccessStatedRequirement : IAuthorizationRequirement;

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
                AccessStatedRequirement => http.GetEndpoint() is not RouteEndpoint,
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

/// <summary>Marks an endpoint any signed-in user may call, without a permission (A5).</summary>
public sealed class SignedInEndpoint;
