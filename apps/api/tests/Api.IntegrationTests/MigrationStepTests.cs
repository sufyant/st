using System.Net;
using Api.Persistence;
using Tenancy;

namespace Api.IntegrationTests;

// Migrations run as a separate step after the bootstrap script, never on application start.
public sealed class MigrationStepTests(Database database)
{
    private const string CatalogExists = "SELECT to_regclass('catalog.tenants') IS NOT NULL";
    private const string MessageStorageExists = "SELECT to_regclass('wolverine.wolverine_incoming_envelopes') IS NOT NULL";

    // Wolverine checks its message storage while it starts, so an application whose database was not migrated does not start,
    // and it creates nothing on the way.
    [Fact]
    public async Task StartApplication_OnADatabaseNotMigrated_FailsAndMigratesNothing()
    {
        var empty = await database.CreateEmptyDatabaseAsync();
        await using var api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application, empty));

        var start = () => api.CreateClient();

        start.ShouldThrow<AggregateException>().Message.ShouldContain("message storage");
        (await database.ScalarAsync<bool>(CatalogExists, database: empty)).ShouldBeFalse();
        (await database.ScalarAsync<bool>(MessageStorageExists, database: empty)).ShouldBeFalse();
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_CreatesEveryModuleAndTheMessageStorageAsTheOwner()
    {
        var empty = await database.CreateEmptyDatabaseAsync();

        var (exitCode, output) = await MigrateCommand.RunAsync(database.ConnectionStringFor(DatabaseRoles.Owner, empty));

        exitCode.ShouldBe(0, output);
        (await OwnerOfAsync("catalog", "tenants", empty)).ShouldBe(DatabaseRoles.Owner);
        (await OwnerOfAsync("wolverine", "wolverine_incoming_envelopes", empty)).ShouldBe(DatabaseRoles.Owner);
        (await OwnerOfAsync("wolverine", "wolverine_queue_messages", empty)).ShouldBe(DatabaseRoles.Owner);
        (await OwnerOfAsync("wolverine", "wolverine_queue_messages_scheduled", empty)).ShouldBe(DatabaseRoles.Owner);
    }

    // The application role owns nothing the migration step creates, yet it can use all of it, the message storage included.
    [Fact]
    public async Task StartApplication_OnAMigratedDatabase_IsReady()
    {
        var empty = await database.CreateEmptyDatabaseAsync();
        (await MigrateCommand.RunAsync(database.ConnectionStringFor(DatabaseRoles.Owner, empty))).ExitCode.ShouldBe(0);
        await using var api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application, empty));

        var ready = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Migrate_WithoutItsOwnConnectionString_Fails()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);

        var migrate = () => MigrationStep.RunAsync(api.Services, TestContext.Current.CancellationToken);

        await migrate.ShouldThrowAsync<InvalidOperationException>();
    }

    // Section 1: each module owns its schema, so no foreign key points from one schema to another.
    [Fact]
    public async Task Migrate_EmptyDatabase_AddsNoForeignKeyAcrossSchemas()
    {
        var migrated = await database.CreateMigratedDatabaseAsync();

        var crossing = await database.ScalarAsSuperuserAsync<string>(
            """
            SELECT coalesce(string_agg(format('%s: %s -> %s', key.conname, key.conrelid::regclass, key.confrelid::regclass), ', '), '')
            FROM pg_constraint AS key
            JOIN pg_class AS source ON source.oid = key.conrelid
            JOIN pg_class AS target ON target.oid = key.confrelid
            WHERE key.contype = 'f' AND source.relnamespace <> target.relnamespace
            """,
            migrated);

        crossing.ShouldBeEmpty();
    }

    private Task<string?> OwnerOfAsync(string schema, string table, string databaseName) =>
        database.ScalarAsync<string>(
            $"SELECT tableowner FROM pg_tables WHERE schemaname = '{schema}' AND tablename = '{table}'", database: databaseName);
}
