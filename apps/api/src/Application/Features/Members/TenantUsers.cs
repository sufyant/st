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

        return tenantDbContext.Users
            .Include(user => user.Roles)
            .SingleOrDefaultAsync(user => user.ExternalUserId == id, cancellationToken);
    }

    public static async Task<OwnerRoster> LoadOwnerRosterAsync(
        TenantDbContext tenantDbContext,
        CancellationToken cancellationToken) =>
        OwnerRoster.Of(await tenantDbContext.Users
            .Where(user => user.Status == UserStatus.Active
                           && user.Roles.Any(role => role.Code == AccessCatalog.OwnerRole.Code))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken));
}
