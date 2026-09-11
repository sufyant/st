using Domain.Shared;
using Domain.Authorization;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

internal static class TenantUsers
{
    public static Task<User?> FindAsync(
        TenantDbContext tenantDbContext,
        string externalUserId,
        CancellationToken cancellationToken)
    {
        var id = ExternalUserId.Create(externalUserId);

        return tenantDbContext.Users.SingleOrDefaultAsync(
            user => user.ExternalUserId == id,
            cancellationToken);
    }

    public static async Task<OwnerRoster> LoadOwnerRosterAsync(
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken) =>
        OwnerRoster.Of(await tenantDbContext.UserRoles
            .Join(
                tenantDbContext.Roles.Where(role => role.Code == AccessCatalog.OwnerRole.Code),
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, _) => assignment.UserId)
            .Join(
                tenantDbContext.Users.Where(user => user.Status == UserStatus.Active),
                ownerId => ownerId,
                user => user.Id,
                (ownerId, _) => ownerId)
            .Distinct()
            .ToListAsync(cancellationToken));
}
