using Application.Abstractions;
using Application.Results;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Roles;

public sealed record TenantRoleDetail(
    string Code,
    string Name,
    string Description,
    string[] PermissionCodes);

[RequiresPermission(TenantPermissions.RolesRead)]
public sealed record ListRolesQuery : IQuery<IReadOnlyList<TenantRoleDetail>>;

public sealed class ListRolesHandler(TenantDbContext tenantDbContext)
    : IRequestHandler<ListRolesQuery, IReadOnlyList<TenantRoleDetail>>
{
    public async Task<Result<IReadOnlyList<TenantRoleDetail>>> HandleAsync(
        ListRolesQuery request,
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

        return Result<IReadOnlyList<TenantRoleDetail>>.Success(details);
    }
}
