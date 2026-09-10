using Domain.Access;
using Domain.Access.Users;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

internal static class TenantUsers
{
    public const string OwnerRoleCode = "owner";

    public static Task<TenantUser?> FindAsync(
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
                tenantDbContext.Roles.Where(role => role.Code == OwnerRoleCode),
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, _) => assignment.UserId)
            .Join(
                tenantDbContext.Users.Where(user => user.Status == TenantUserStatus.Active),
                ownerId => ownerId,
                user => user.Id,
                (ownerId, _) => ownerId)
            .Distinct()
            .ToListAsync(cancellationToken));
}
