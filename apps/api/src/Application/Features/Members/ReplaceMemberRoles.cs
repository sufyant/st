using Application.Abstractions;
using Application.Results;
using Domain.Authorization;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

[RequiresPermission(TenantPermissions.RolesManage)]
public sealed record ReplaceMemberRolesCommand(string ExternalUserId, string[] RoleCodes) : ICommand<Unit>;

public sealed class ReplaceMemberRolesHandler(TenantDbContext tenantDbContext)
    : IRequestHandler<ReplaceMemberRolesCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        ReplaceMemberRolesCommand request,
        CancellationToken cancellationToken)
    {
        var user = await TenantUsers.FindAsync(tenantDbContext, request.ExternalUserId, cancellationToken);

        if (user is null)
        {
            return Result<Unit>.Failure(MemberErrors.Missing);
        }

        var roles = await tenantDbContext.Roles
            .Where(role => request.RoleCodes.Contains(role.Code))
            .ToListAsync(cancellationToken);

        if (roles.Count != request.RoleCodes.Distinct().Count())
        {
            return Result<Unit>.Failure(Error.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.RoleCodes)] = ["One or more roles do not exist."]
            }));
        }

        if (!roles.Any(role => role.Code == AccessCatalog.OwnerRole.Code))
        {
            if (await Owners.IsLastOwnerAsync(tenantDbContext, user, cancellationToken))
            {
                return Result<Unit>.Failure(MemberErrors.LastOwner);
            }
        }

        user.AssignRoles(roles);

        return Result.Success();
    }
}
