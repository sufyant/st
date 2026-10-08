using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Tenancy.IntegrationTests.Database))]

namespace Tenancy.IntegrationTests;

// One PostgreSQL 18 server for the test assembly, the version of the Neon project (0011), set up the way a deployment is:
// the bootstrap script creates the roles, then the owner creates the tables (0018, 0020).
public sealed class Database : IAsyncLifetime
{
    private const string Password = "test-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public string OwnerConnectionString => ConnectionStringFor(DatabaseRoles.Owner);

    public string ApplicationConnectionString => ConnectionStringFor(DatabaseRoles.Application);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await RunBootstrapScriptAsync();
        await CreateNotesAsOwnerAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private async Task RunBootstrapScriptAsync()
    {
        await _container.CopyAsync(await File.ReadAllBytesAsync("bootstrap.sql"), "/tmp/bootstrap.sql");

        var result = await _container.ExecAsync(
        [
            "psql", "--username", "postgres", "--dbname", new NpgsqlConnectionStringBuilder(_container.GetConnectionString()).Database!,
            "-v", $"owner_password={Password}", "-v", $"application_password={Password}", "-v", $"reporting_password={Password}",
            "--file", "/tmp/bootstrap.sql",
        ]);

        result.ExitCode.ShouldBe(0, result.Stderr);
    }

    // The notes have no migration of their own; creating the tables goes through the same SQL generator a migration does.
    private async Task CreateNotesAsOwnerAsync()
    {
        await using (var notes = new NotesDbContext(
            TenancyServiceCollectionExtensions.ModuleDbContextOptions<NotesDbContext>(NotesDbContext.Schema, OwnerConnectionString),
            new TenantContext()))
        {
            await notes.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();
        await using var grant = new NpgsqlCommand(
            $"""
            GRANT USAGE ON SCHEMA {NotesDbContext.Schema} TO {DatabaseRoles.Application};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {NotesDbContext.Schema} TO {DatabaseRoles.Application};
            """,
            connection);
        await grant.ExecuteNonQueryAsync();
    }

    private string ConnectionStringFor(string role) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Username = role, Password = Password }.ConnectionString;
}
