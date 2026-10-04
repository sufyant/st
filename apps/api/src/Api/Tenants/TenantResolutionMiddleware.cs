using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Serilog;
using Serilog.Context;
using Tenancy;
using Wolverine;

namespace Api.Tenants;

// Resolves the tenant of a tenant-scoped request from the slug in its path and the user's membership, never from the client's
// word alone (0015). It rejects nothing: the rate limiter runs next and limits non-members by user or address (0035), and
// TenantRequirementFilter turns them away afterwards.
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    private const string TenantTag = "tenant.id";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantDirectory directory,
        TenantContext tenant,
        IMessageBus bus,
        IDiagnosticContext diagnostics)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<TenantScopedEndpoint>() is null
            || context.GetRouteValue(TenantRoutes.SlugParameter) is not string slug
            || context.User.FindFirstValue(ClaimTypes.NameIdentifier) is not { } userId
            || await directory.FindMemberTenantAsync(slug, userId, context.RequestAborted) is not { } tenantId)
        {
            await next(context);
            return;
        }

        tenant.Set(tenantId);

        // Commands sent from the request carry the tenant in their envelope; the transaction middleware sets it there (0016).
        bus.TenantId = tenantId.ToString();

        // Every signal carries the tenant (0039).
        var id = tenantId.ToString();
        diagnostics.Set("TenantId", id);
        Activity.Current?.SetTag(TenantTag, id);
        context.Features.Get<IHttpMetricsTagsFeature>()?.Tags.Add(new(TenantTag, id));
        using (LogContext.PushProperty("TenantId", id))
        {
            await next(context);
        }
    }
}
