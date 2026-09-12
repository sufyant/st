using Application.Abstractions;
using Application.Results;
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
            return MemberErrors.Missing;
        }

        var roles = await tenantDbContext.Roles
            .Where(role => request.RoleCodes.Contains(role.Code))
            .ToListAsync(cancellationToken);

        if (roles.Count != request.RoleCodes.Distinct().Count())
        {
            return Error.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.RoleCodes)] = ["One or more roles do not exist."]
            });
        }

        user.AssignRoles(roles);

        return Result.Success();
    }
}
