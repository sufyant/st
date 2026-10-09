using ControlPlane.Contracts;
using ControlPlane.Domain.SystemAdmins;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Infrastructure;

internal sealed class SystemAdminDirectory(CatalogDbContext catalog) : ISystemAdminDirectory
{
    public async Task<IReadOnlySet<string>?> FindSystemPermissionsAsync(
        string externalUserId,
        bool secondFactorVerified,
        CancellationToken cancellationToken) =>
        SystemDoor.PermissionsFor(
            await catalog.SystemAdmins.AnyAsync(
                admin => catalog.Users.Any(user => user.Id == admin.UserId && user.ExternalId == externalUserId), cancellationToken),
            secondFactorVerified);
}
