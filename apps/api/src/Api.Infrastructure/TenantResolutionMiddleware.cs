using System.Security.Claims;
using Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Infrastructure;

/// <summary>
/// Read this before adding a new tenant-scoped route: this middleware only resolves
/// tenant/membership for routes whose template declares a route parameter named exactly
/// "tenant-alias" (see <c>context.GetRouteValue("tenant-alias")</c> below). A tenant-scoped
/// route that uses a different parameter name will silently bypass both tenant resolution
/// and membership enforcement, with no error - the request proceeds as if it were not
/// tenant-scoped at all.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AdminDbContext dbContext, IMemoryCache cache)
    {
        var alias = context.GetRouteValue("tenant-alias") as string;

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

        var cacheKey = $"tenant-resolution:{slug.Value}";
        if (!cache.TryGetValue(cacheKey, out Tenant? tenant))
        {
            tenant = await dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
            if (tenant is not null)
            {
                cache.Set(cacheKey, tenant, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) });
            }
        }

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
                foreach (var claimType in new[] { "tenant_id", "tenant_slug", "membership_role", "permission" })
                {
                    foreach (var existingClaim in identity.FindAll(claimType).ToList())
                    {
                        identity.TryRemoveClaim(existingClaim);
                    }
                }

                identity.AddClaim(new Claim("tenant_id", tenant.Id.ToString()));
                identity.AddClaim(new Claim("tenant_slug", tenant.Slug.Value));
                identity.AddClaim(new Claim("membership_role", membership.Role));

                var permissions = await dbContext.RolePermissions.AsNoTracking()
                    .Where(rp => rp.Role == membership.Role)
                    .Select(rp => rp.Permission)
                    .ToListAsync();

                foreach (var permission in permissions)
                {
                    identity.AddClaim(new Claim("permission", permission));
                }
            }
        }

        await next(context);
    }
}
