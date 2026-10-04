using ControlPlane.Domain.SystemAdmins;
using Microsoft.EntityFrameworkCore;
using Tenancy;

namespace ControlPlane.Infrastructure;

internal sealed class SystemAdminDirectory(CatalogDbContext catalog) : ISystemAdminDirectory
{
    public async Task<IReadOnlySet<string>?> FindSystemPermissionsAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var role = await catalog.SystemAdmins
            .Where(admin => catalog.Users.Any(user => user.Id == admin.UserId && user.ExternalId == externalUserId))
            .Select(admin => (SystemRole?)admin.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return role is { } granted ? SystemRoles.PermissionsOf(granted) : null;
    }
}
