using System.Diagnostics;
using System.Security.Claims;
using Api.Authentication;
using Api.Authorization;
using Api.SystemAdmins;
using ControlPlane.Contracts;
using Microsoft.AspNetCore.Http.Features;
using Serilog;
using Serilog.Context;
using Wolverine;

namespace Api.Tenants;

// Resolves the tenant of a request and what the caller may do there, never from the client's word alone. On a tenant
// route the tenant comes from the id in the path and the user's membership, with the permissions of the member's role; on a system
// route ControlPlane answers with the user's system permissions, given whether the session verified a second factor (A6).
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
            access.SystemPermissions = await systemAdmins.FindSystemPermissionsAsync(
                userId, context.User.HasVerifiedSecondFactor(), context.RequestAborted);
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

        // Commands sent from the request carry the tenant in their envelope, and their handlers' transactions declare it (W2, W3).
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
