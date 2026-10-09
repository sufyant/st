using System.Diagnostics;
using System.Security.Claims;
using Api.SystemAdmins;
using Api.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Serilog;
using Serilog.Context;
using Tenancy;
using Wolverine;

namespace Api.Tenants;

// Resolves the tenant of a request and what the caller may do there, never from the client's word alone. On a tenant
// route the tenant comes from the id in the path and the user's membership, with the permissions of the member's role; on a system
// route the user's system permissions are read.
// It rejects nothing: the rate limiter runs next and limits the others by user or address, and authorization turns them
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

        Guid? tenantId = null;
        if (metadata.GetMetadata<SystemEndpoint>() is not null)
        {
            access.SystemPermissions = await systemAdmins.FindSystemPermissionsAsync(userId, context.RequestAborted);
        }
        else if (metadata.GetMetadata<TenantScopedEndpoint>() is not null
            && Guid.TryParse(context.GetRouteValue(TenantRoutes.TenantIdParameter) as string, out var requested))
        {
            access.Membership = await directory.FindMembershipAsync(requested, userId, context.RequestAborted);
            tenantId = access.Membership?.TenantId;
        }

        if (tenantId is not { } resolved)
        {
            await next(context);
            return;
        }

        tenant.Set(resolved);

        // Commands sent from the request carry the tenant in their envelope; the transaction middleware sets it there.
        bus.TenantId = resolved.ToString();

        // Every signal carries the tenant.
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
