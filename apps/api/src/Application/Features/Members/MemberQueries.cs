using Domain.Shared;
using Domain.Authorization;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

internal static class MemberQueries
{
    public static Task<User?> FindAsync(
        TenantDbContext tenantDbContext,
        string externalUserId,
        CancellationToken cancellationToken)
    {
        var id = ExternalUserId.Create(externalUserId);

        return tenantDbContext.Users
            .Include(user => user.Role)
            .SingleOrDefaultAsync(user => user.ExternalUserId == id, cancellationToken);
    }
}
