using System.Security.Claims;
using Api.Authorization;
using Domain.Shared;
using Domain.Authorization;
using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence.Tenants;
using Infrastructure.Tenants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;

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

        tenantContext.Set(TenantId.From(tenant.Id), tenant.Alias, tenant.DatabaseName);

        var userId = ExternalUserId.Create(externalUserId);
        var tenantDbContext = services.GetRequiredService<TenantDbContext>();
        var tenantUser = await tenantDbContext.Users
            .Include(user => user.Roles)
            .ThenInclude(role => role.Permissions)
            .SingleOrDefaultAsync(user => user.ExternalUserId == userId, context.RequestAborted);

        if (tenantUser?.Status != UserStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var permissions = tenantUser.Roles
            .SelectMany(role => role.Permissions)
            .Select(permission => permission.Code)
            .Distinct()
            .ToList();
        var identity = new ClaimsIdentity("Tenant");
        identity.AddClaim(new Claim(TenantClaims.TenantId, tenant.Id.ToString("N")));

        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(TenantClaims.Permission, permission));
        }

        context.User.AddIdentity(identity);

        await next(context);
    }
}
