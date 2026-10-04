using System.Diagnostics;
using System.Security.Claims;
using Api.Admin;
using Api.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Serilog;
using Serilog.Context;
using SharedKernel;
using Tenancy;
using Wolverine;

namespace Api.Tenants;

// Resolves the tenant of a request and what the caller may do there, never from the client's word alone (0015). On a tenant
// route the tenant comes from the slug and the user's membership, with the permissions of the member's role (0030); on an admin
// route the user's system permissions are read, and a system admin allowed to enter tenants enters the one in the path (0031).
// It rejects nothing: the rate limiter runs next and limits the others by user or address (0035), and authorization turns them
// away afterwards.
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    private const string TenantTag = "tenant.id";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantDirectory directory,
        ISystemAdminDirectory systemAdmins,
        RequestAccess access,
        TenantContext tenant,
        IMessageBus bus,
        IDiagnosticContext diagnostics)
    {
        var metadata = context.GetEndpoint()?.Metadata;
        if (metadata is null || context.User.FindFirstValue(ClaimTypes.NameIdentifier) is not { } userId)
        {
            await next(context);
            return;
        }

        var slug = context.GetRouteValue(TenantRoutes.SlugParameter) as string;
        Guid? tenantId = null;
        if (metadata.GetMetadata<SystemAdminEndpoint>() is not null)
        {
            access.SystemPermissions = await systemAdmins.FindSystemPermissionsAsync(userId, context.RequestAborted);
            if (metadata.GetMetadata<AdminTenantScopedEndpoint>() is not null && slug is not null && access.Has(Permissions.SystemTenantsEnter))
            {
                tenantId = access.AdminTenantId = await directory.FindTenantAsync(slug, context.RequestAborted);
            }
        }
        else if (metadata.GetMetadata<TenantScopedEndpoint>() is not null && slug is not null)
        {
            access.Membership = await directory.FindMembershipAsync(slug, userId, context.RequestAborted);
            tenantId = access.Membership?.TenantId;
        }

        if (tenantId is not { } resolved)
        {
            await next(context);
            return;
        }

        tenant.Set(resolved);

        // Commands sent from the request carry the tenant in their envelope; the transaction middleware sets it there (0016).
        bus.TenantId = resolved.ToString();

        // Every signal carries the tenant (0039).
        var id = resolved.ToString();
        diagnostics.Set("TenantId", id);
        Activity.Current?.SetTag(TenantTag, id);
        context.Features.Get<IHttpMetricsTagsFeature>()?.Tags.Add(new(TenantTag, id));
        using (LogContext.PushProperty("TenantId", id))
        {
            await next(context);
        }
    }
}
