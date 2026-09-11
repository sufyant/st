using Domain.Authorization;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

// Son owner'ı kaybeden bir tenant yalnızca veritabanına elle müdahaleyle kurtarılabilir,
// o yüzden üç yazma yolu da buradan geçer.
internal static class Owners
{
    public static async Task<bool> IsLastOwnerAsync(
        TenantDbContext tenantDbContext,
        User user,
        CancellationToken cancellationToken)
    {
        var ownerCode = AccessCatalog.OwnerRole.Code;

        if (!user.Roles.Any(role => role.Code == ownerCode))
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
