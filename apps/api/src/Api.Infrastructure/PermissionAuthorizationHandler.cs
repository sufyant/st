using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class PermissionAuthorizationHandler(AdminDbContext dbContext)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var role = context.User.FindFirst("membership_role")?.Value;

        if (string.IsNullOrEmpty(role))
        {
            return;
        }

        var hasPermission = await dbContext.RolePermissions
            .AsNoTracking()
            .AnyAsync(rp => rp.Role == role && rp.Permission == requirement.Permission);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
