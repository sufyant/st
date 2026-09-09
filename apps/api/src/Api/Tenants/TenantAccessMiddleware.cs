using System.Security.Claims;
using Domain.Access;
using Domain.Access.Users;
using Domain.Tenants;
using Infrastructure.Persistence.Admin;
using Infrastructure.Persistence.Tenants;
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

        var services = context.RequestServices;
        var adminDbContext = services.GetRequiredService<AdminDbContext>();
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

        var tenant = await adminDbContext.Tenants.SingleOrDefaultAsync(
            candidate => candidate.Alias == alias,
            context.RequestAborted);

        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var userId = ExternalUserId.Create(externalUserId);
        var hasMembership = await adminDbContext.Memberships.AnyAsync(
            membership => membership.TenantId == tenant.Id && membership.ExternalUserId == userId,
            context.RequestAborted);

        if (!hasMembership)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var tenantDbContextFactory = services.GetRequiredService<TenantDbContextFactory>();
        await using var tenantDbContext = tenantDbContextFactory.Create(tenant);
        var tenantUser = await tenantDbContext.Users.SingleOrDefaultAsync(
            user => user.ExternalUserId == userId,
            context.RequestAborted);

        if (tenantUser?.Status != TenantUserStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        tenantContext.Set(tenant);
        context.User.AddIdentity(new ClaimsIdentity([new Claim("tenant_id", tenant.Id.ToString("N"))], "Tenant"));

        await next(context);
    }
}
