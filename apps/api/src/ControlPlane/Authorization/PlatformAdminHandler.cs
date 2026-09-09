using System.Security.Claims;
using Domain.Access;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Authorization;

public sealed class PlatformAdminHandler(ControlPlaneDbContext dbContext)
    : AuthorizationHandler<PlatformAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformAdminRequirement requirement)
    {
        var externalUserId = context.User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(externalUserId))
        {
            return;
        }

        var userId = ExternalUserId.Create(externalUserId);
        var isPlatformAdmin = await dbContext.PlatformAdmins
            .AsNoTracking()
            .AnyAsync(admin => admin.ExternalUserId == userId);

        if (isPlatformAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
