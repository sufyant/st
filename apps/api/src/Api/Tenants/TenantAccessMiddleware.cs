using System.Security.Claims;
using Domain.Access;
using Domain.Access.Users;
using Domain.Tenants;
using Infrastructure.Persistence.Tenants;
using Infrastructure.Tenants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Api.Tenants;

public sealed class TenantAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var tenantAlias = context.Request.RouteValues["tenantAlias"]?.ToString();

        if (string.IsNullOrWhiteSpace(tenantAlias))
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync();
            return;
        }

        var externalUserId = context.User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(externalUserId))
        {
            await context.ChallengeAsync();
            return;
        }

        TenantAlias alias;

        try
        {
            alias = TenantAlias.Create(tenantAlias);
        }
        catch (ArgumentException)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var services = context.RequestServices;
        var resolver = services.GetRequiredService<TenantResolver>();
        var tenant = await resolver.ResolveAsync(alias.Value, context.RequestAborted);

        if (tenant is null || tenant.Status is TenantStatus.Deprovisioning or TenantStatus.Deleted)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (tenant.Status is TenantStatus.Provisioning)
        {
            context.Response.Headers.RetryAfter = "10";
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (tenant.Status is TenantStatus.Suspended)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var hasMembership = await resolver.HasMembershipAsync(tenant.Id, externalUserId, context.RequestAborted);

        if (!hasMembership)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        tenantContext.Set(tenant.Id, tenant.Alias, tenant.DatabaseName);

        var userId = ExternalUserId.Create(externalUserId);
        var tenantDbContext = services.GetRequiredService<TenantDbContext>();
        var tenantUser = await tenantDbContext.Users.SingleOrDefaultAsync(
            user => user.ExternalUserId == userId,
            context.RequestAborted);

        if (tenantUser?.Status != TenantUserStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.User.AddIdentity(new ClaimsIdentity([new Claim("tenant_id", tenant.Id.ToString("N"))], "Tenant"));

        await next(context);
    }
}
