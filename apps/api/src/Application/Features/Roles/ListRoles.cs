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
        var roles = await tenantDbContext.Roles
            .Include(role => role.Permissions)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var details = roles
            .Select(role => new TenantRoleDetail(
                role.Code,
                role.Name,
                role.Description,
                role.Permissions
                    .Select(permission => permission.Code)
                    .OrderBy(code => code)
                    .ToArray()))
            .OrderBy(role => role.Code)
            .ToList();

        return Result<IReadOnlyList<TenantRoleDetail>>.Success(details);
    }
}
