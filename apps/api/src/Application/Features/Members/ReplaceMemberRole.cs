using Application.Abstractions;
using Application.Results;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

[RequiresPermission(TenantPermissions.RolesManage)]
public sealed record ReplaceMemberRoleCommand(string ExternalUserId, string RoleCode) : ICommand<Unit>;

public sealed class ReplaceMemberRoleHandler(TenantDbContext tenantDbContext)
    : IRequestHandler<ReplaceMemberRoleCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        ReplaceMemberRoleCommand request,
        CancellationToken cancellationToken)
    {
        var user = await MemberQueries.FindAsync(tenantDbContext, request.ExternalUserId, cancellationToken);

        if (user is null)
        {
            return MemberErrors.Missing;
        }

        var role = await tenantDbContext.Roles
            .SingleOrDefaultAsync(role => role.Code == request.RoleCode, cancellationToken);

        if (role is null)
        {
            return Error.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.RoleCode)] = [$"Role '{request.RoleCode}' does not exist."]
            });
        }

        user.AssignRole(role);

        return Result.Success();
    }
}
