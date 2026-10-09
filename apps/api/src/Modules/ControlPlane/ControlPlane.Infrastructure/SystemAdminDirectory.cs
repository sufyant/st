using ControlPlane.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace ControlPlane.Infrastructure;

// A system admin holds the whole system pool.
internal sealed class SystemAdminDirectory(CatalogDbContext catalog) : ISystemAdminDirectory
{
    public async Task<IReadOnlySet<string>?> FindSystemPermissionsAsync(string externalUserId, CancellationToken cancellationToken) =>
        await catalog.SystemAdmins.AnyAsync(
            admin => catalog.Users.Any(user => user.Id == admin.UserId && user.ExternalId == externalUserId), cancellationToken)
            ? Permissions.SystemPool
            : null;
}
