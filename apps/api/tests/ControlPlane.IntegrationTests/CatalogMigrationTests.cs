using System.Security.Cryptography;
using System.Text;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenancy;

namespace ControlPlane.IntegrationTests;

// Data an existing database may hold from before a migration.
public sealed class CatalogMigrationTests(Database database)
{
    private const string BeforeExpiredWasRemoved = "20261008213132_RemoveCustomRolesAndSystemRoles";

    private const string BeforeTenantsHadAName = "20261009075343_IsolateMembershipsAndInvitations";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // The Expired status is gone: expiry is checked when an invitation is read, so an old expired invitation becomes pending and
    // stays unusable.
    [Fact]
    public async Task MigrateCatalog_AnInvitationThatWasExpired_IsPendingAndAcceptingItIsRejectedAsExpired()
    {
        var name = await database.CreateEmptyDatabaseAsync();
        await MigrateCatalogAsync(name, BeforeExpiredWasRemoved);
        var (tenantId, inviterId, invitationId, email) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Unique.Email());
        const string secret = "old-secret";
        await database.ExecuteAsSuperuserAsync(
            $"""
            INSERT INTO catalog.tenants (id, slug, status) VALUES ('{tenantId}', '{Unique.Slug()}', 'Active');
            INSERT INTO catalog.users (id, external_id) VALUES ('{inviterId}', '{Unique.ExternalId()}');
            INSERT INTO catalog.invitations (id, tenant_id, email, role_id, invited_by, created_at, expires_at, status, token_hash)
            SELECT '{invitationId}', '{tenantId}', '{email}', roles.id, '{inviterId}', '2026-09-01T09:00:00Z', '2026-09-08T09:00:00Z',
                'Expired', '{Sha256(secret)}'
            FROM catalog.roles WHERE roles.built_in = 'Owner';
            """,
            name);

        await using var services = database.BuildServices(database: name);
        await services.GetServices<IModuleMigrator>().Single()
            .MigrateAsync(services, database.ConnectionStringFor(DatabaseRoles.Owner, name), Cancellation);

        (await database.ScalarAsSuperuserAsync<string>($"SELECT status FROM catalog.invitations WHERE id = '{invitationId}'", name))
            .ShouldBe("Pending");
        var invitee = Unique.ExternalId();
        database.Identity.AddAccount(invitee, email);
        var accepted = await Handlers.AcceptAsync(services, $"{tenantId}.{secret}", invitee);
        accepted.Error.Code.ShouldBe("invitation.expired");
    }

    [Fact]
    public async Task MigrateCatalog_ATenantFromBeforeNames_IsNamedAfterItsSlug()
    {
        var name = await database.CreateEmptyDatabaseAsync();
        await MigrateCatalogAsync(name, BeforeTenantsHadAName);
        var tenantId = Guid.CreateVersion7();
        await database.ExecuteAsSuperuserAsync($"INSERT INTO catalog.tenants (id, slug, status) VALUES ('{tenantId}', 'old-acme', 'Active')", name);

        await using var services = database.BuildServices(database: name);
        await services.GetServices<IModuleMigrator>().Single()
            .MigrateAsync(services, database.ConnectionStringFor(DatabaseRoles.Owner, name), Cancellation);

        (await database.ScalarAsSuperuserAsync<string>($"SELECT name FROM catalog.tenants WHERE id = '{tenantId}'", name)).ShouldBe("old-acme");
        (await database.ScalarAsSuperuserAsync<string>(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'catalog' AND table_name = 'tenants' AND column_name = 'name'",
            name)).ShouldBe("NO");
    }

    private async Task MigrateCatalogAsync(string databaseName, string targetMigration)
    {
        await using var catalog = new CatalogDbContext(TenancyServiceCollectionExtensions.ModuleDbContextOptions<CatalogDbContext>(
            CatalogDbContext.Schema, database.ConnectionStringFor(DatabaseRoles.Owner, databaseName)));

        await catalog.Database.MigrateAsync(targetMigration, Cancellation);
    }

    private static string Sha256(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
