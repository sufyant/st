using ControlPlane.Application.Ports;
using ControlPlane.Contracts;
using ControlPlane.Domain.SystemAdmins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure;

internal sealed class SystemAdminDirectory(
    CatalogDbContext catalog,
    IIdentityProvider identity,
    IOptions<SystemAdminSettings> settings,
    TimeProvider time) : ISystemAdminDirectory
{
    public async Task<IReadOnlySet<string>?> FindSystemPermissionsAsync(
        string externalUserId,
        bool secondFactorVerified,
        CancellationToken cancellationToken)
    {
        var isSystemAdmin = await IsSystemAdminAsync(externalUserId, cancellationToken);
        if (!isSystemAdmin && await MayBecomeTheFirstAsync(externalUserId, secondFactorVerified, cancellationToken))
        {
            await GrantTheFirstAsync(externalUserId, cancellationToken);
            isSystemAdmin = await IsSystemAdminAsync(externalUserId, cancellationToken);
        }

        return SystemDoor.PermissionsFor(isSystemAdmin, secondFactorVerified);
    }

    private Task<bool> IsSystemAdminAsync(string externalUserId, CancellationToken cancellationToken) =>
        catalog.SystemAdmins.AnyAsync(admin => catalog.Users.Any(user => user.Id == admin.UserId && user.ExternalId == externalUserId), cancellationToken);

    // The verified addresses come from the identity provider's server, as when an invitation is accepted, and only while nobody holds
    // the door yet.
    private async Task<bool> MayBecomeTheFirstAsync(string externalUserId, bool secondFactorVerified, CancellationToken cancellationToken) =>
        secondFactorVerified
        && settings.Value is { NamesTheFirstSystemAdmin: true, FirstSystemAdminEmail: var email }
        && !await catalog.SystemAdmins.AnyAsync(cancellationToken)
        && SystemDoor.IsFirstSystemAdmin(email!, secondFactorVerified, await identity.FindVerifiedEmailsAsync(externalUserId, cancellationToken));

    // One statement writes the catalog user, if the catalog does not know them yet, and the grant, and it writes the grant only while
    // the staff list is empty: of two pods that let the same person in at the same moment, one writes it.
    private async Task GrantTheFirstAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await catalog.Database.ExecuteSqlAsync(
            $"""
            WITH added AS (
                INSERT INTO catalog.users (id, external_id) VALUES ({Guid.CreateVersion7(now)}, {externalUserId})
                ON CONFLICT (external_id) DO NOTHING
                RETURNING id),
            person AS (
                SELECT id FROM added
                UNION ALL SELECT id FROM catalog.users WHERE external_id = {externalUserId})
            INSERT INTO catalog.system_admins (user_id, granted_by, granted_at)
            SELECT id, NULL, {now} FROM person
            WHERE NOT EXISTS (SELECT FROM catalog.system_admins)
            ON CONFLICT (user_id) DO NOTHING
            """,
            cancellationToken);
    }
}
