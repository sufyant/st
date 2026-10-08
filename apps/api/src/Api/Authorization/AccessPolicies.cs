using System.Security.Claims;
using Api.Authentication;
using Audit.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Api.Authorization;

// Authorization asks for permissions, never roles (0030). An endpoint names the permission it needs as its policy; the route
// groups add who may reach them at all.
internal static partial class AccessPolicies
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
    // Every denial of a signed-in user is recorded (0040): in the tenant's audit log when the request entered a tenant, otherwise as
    // a security event in the log, because the audit log is tenant-scoped (0039).
    private sealed partial class AccessDeniedHandler(ILoggerFactory loggers, TimeProvider time) : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();
        private readonly ILogger _security = loggers.CreateLogger("Api.Security");

        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Forbidden)
            {
                await RecordDenialAsync(context);
            }

            if (authorizeResult.Forbidden && TenantNotFound(context))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await _default.HandleAsync(next, context, policy, authorizeResult);
        }

        private async Task RecordDenialAsync(HttpContext context)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var access = context.RequestServices.GetRequiredService<RequestAccess>();
            var operation = $"{context.Request.Method} {context.Request.Path}";
            if (userId is not null && access.Membership is not null)
            {
                // Tenant resolution has put the tenant on the request's bus, so the record is stored under it.
                await AuditTrail.RecordDeniedAsync(context.RequestServices.GetRequiredService<IMessageBus>(), time, userId, operation);
                return;
            }

            LogDenial(_security, userId, operation);
        }

        private static bool TenantNotFound(HttpContext context)
        {
            var metadata = context.GetEndpoint()?.Metadata;
            var access = context.RequestServices.GetRequiredService<RequestAccess>();

            return metadata?.GetMetadata<Tenants.TenantScopedEndpoint>() is not null && access.Membership is null;
        }

        [LoggerMessage(EventName = "AuthorizationDenied", Level = LogLevel.Warning, Message = "Authorization denied to {UserId}: {Operation}")]
        private static partial void LogDenial(ILogger logger, string? userId, string operation);
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
