using Domain.Authorization;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

// A tenant whose last owner is removed, disabled or demoted can only be recovered by editing the
// database by hand, so the rule lives here rather than in whichever handler happens to need it.
internal static class Owners
{
    public static async Task<bool> IsLastOwnerAsync(
        TenantDbContext tenantDbContext,
        User user,
        CancellationToken cancellationToken)
    {
        var ownerCode = AccessCatalog.OwnerRole.Code;

        var holdsOwnerRole = await tenantDbContext.Users.AnyAsync(
            candidate => candidate.Id == user.Id && candidate.Roles.Any(role => role.Code == ownerCode),
            cancellationToken);

        if (!holdsOwnerRole)
        {
            return false;
        }

        var otherOwners = await tenantDbContext.Users
            .Where(candidate => candidate.Id != user.Id
                                && candidate.Status == UserStatus.Active
                                && candidate.Roles.Any(role => role.Code == ownerCode))
            .CountAsync(cancellationToken);

        return otherOwners == 0;
    }
}
