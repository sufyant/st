using ControlPlane.Authorization;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane;

public static class ControlPlaneEndpoints
{
    public const string PolicyName = "RequirePlatformAdmin";

    public static IServiceCollection AddControlPlane(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PlatformAdminHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyName, policy => policy.AddRequirements(new PlatformAdminRequirement()));

        return services;
    }

    public static IEndpointRouteBuilder MapControlPlane(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/api/v1").RequireAuthorization(PolicyName);

        group.MapGet("/tenants", async (ControlPlaneDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var tenants = await dbContext.Tenants
                .AsNoTracking()
                .OrderByDescending(tenant => tenant.CreatedAt)
                .Select(tenant => new TenantSummary(
                    tenant.Id,
                    tenant.Alias.Value,
                    tenant.Status.ToString(),
                    tenant.CreatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(tenants);
        });

        return endpoints;
    }
}

public sealed record TenantSummary(Guid Id, string Alias, string Status, DateTimeOffset CreatedAt);
