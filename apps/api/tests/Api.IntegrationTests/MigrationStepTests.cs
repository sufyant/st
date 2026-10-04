using Api.Persistence;
using Tenancy;

namespace Api.IntegrationTests;

// Migrations run as a separate step after the bootstrap script, never on application start (0020).
public sealed class MigrationStepTests(Database database)
{
    private const string CatalogExists = "SELECT to_regclass('catalog.tenants') IS NOT NULL";

    [Fact]
    public async Task Starting_the_application_does_not_migrate()
    {
        var empty = await database.CreateEmptyDatabaseAsync();
        await using var api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application, empty));

        await api.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);

        (await database.ScalarAsync<bool>(CatalogExists, database: empty)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_migration_step_migrates_every_module_as_the_owner()
    {
        var empty = await database.CreateEmptyDatabaseAsync();
        await using var api = new ApiFactory(
            database.ConnectionStringFor(DatabaseRoles.Application, empty),
            database.ConnectionStringFor(DatabaseRoles.Owner, empty));

        await MigrationStep.RunAsync(api.Services, TestContext.Current.CancellationToken);

        (await database.ScalarAsync<bool>(CatalogExists, database: empty)).ShouldBeTrue();
        (await database.ScalarAsync<string>("SELECT tableowner FROM pg_tables WHERE schemaname = 'catalog' AND tablename = 'tenants'", database: empty))
            .ShouldBe(DatabaseRoles.Owner);
    }

    [Fact]
    public async Task The_migration_step_needs_its_own_connection_string()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);

        var migrate = () => MigrationStep.RunAsync(api.Services, TestContext.Current.CancellationToken);

        await migrate.ShouldThrowAsync<InvalidOperationException>();
    }
}
