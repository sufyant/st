using System.Security.Claims;
using Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AdminDbContext dbContext)
    {
        var alias = context.GetRouteValue("tenant") as string;

        if (string.IsNullOrEmpty(alias))
        {
            await next(context);
            return;
        }

        TenantSlug slug;
        try
        {
            slug = TenantSlug.Create(alias);
        }
        catch (ArgumentException)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug);

        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var clerkUserId = context.User.FindFirstValue("sub");
            var user = clerkUserId is null
                ? null
                : await dbContext.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);

            var membership = user is null
                ? null
                : await dbContext.Memberships.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == tenant.Id);

            if (membership is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (context.User.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim("tenant_id", tenant.Id.ToString()));
                identity.AddClaim(new Claim("tenant_slug", tenant.Slug.Value));
                identity.AddClaim(new Claim("membership_role", membership.Role));
            }
        }

        await next(context);
    }
}
