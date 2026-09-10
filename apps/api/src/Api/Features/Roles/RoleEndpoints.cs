using Application.Abstractions;
using Api.Authorization;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Roles;

public sealed record TenantRoleDetail(
    string Code,
    string Name,
    string Description,
    string[] PermissionCodes);

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapTenantRoles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{tenantAlias}/api/v1/roles", ListAsync)
            .RequirePermission(TenantPermissions.RolesRead);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken)
    {
        var roles = await tenantDbContext.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var grants = await tenantDbContext.RolePermissions
            .AsNoTracking()
            .Join(
                tenantDbContext.Permissions,
                rolePermission => rolePermission.PermissionId,
                permission => permission.Id,
                (rolePermission, permission) => new { rolePermission.RoleId, permission.Code })
            .ToListAsync(cancellationToken);
        var details = roles
            .Select(role => new TenantRoleDetail(
                role.Code ?? string.Empty,
                role.Name,
                role.Description,
                grants
                    .Where(grant => grant.RoleId == role.Id)
                    .Select(grant => grant.Code)
                    .OrderBy(code => code)
                    .ToArray()))
            .OrderBy(role => role.Code)
            .ToList();

        return Results.Ok(details);
    }
}
