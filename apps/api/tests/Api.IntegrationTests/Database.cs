using ControlPlane.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenancy;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Api.IntegrationTests.Database))]

namespace Api.IntegrationTests;

// One PostgreSQL 18 server for the test assembly (0011). Its main database is set up the way a deployment is: the bootstrap
// script, then every module's migrations as the owner (0018, 0020). The probe tables stand in for a module's tenant entity.
public sealed class Database : IAsyncLifetime
{
    private const string Password = "test-password";
    private const string MainDatabase = "api";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").WithDatabase(MainDatabase).Build();

    public string ApplicationConnectionString => ConnectionStringFor(DatabaseRoles.Application);

    public string OwnerConnectionString => ConnectionStringFor(DatabaseRoles.Owner);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await _container.CopyAsync(await File.ReadAllBytesAsync("bootstrap.sql"), "/tmp/bootstrap.sql");
        await RunBootstrapScriptAsync(MainDatabase);

        await using var services = new ServiceCollection()
            .AddTenancy(_ => ApplicationConnectionString)
            .AddControlPlaneModule()
            .BuildServiceProvider();
        foreach (var migrator in services.GetServices<IModuleMigrator>())
        {
            await migrator.MigrateAsync(services, OwnerConnectionString, CancellationToken.None);
        }

        await CreateProbesAsOwnerAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public string ConnectionStringFor(string role, string database = MainDatabase) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = role,
            Password = Password,
            Database = database,
        }.ConnectionString;

    public string SuperuserConnectionString =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = MainDatabase }.ConnectionString;

    // A role the bootstrap script never creates, for proving that the application refuses to run as one.
    public async Task<string> CreateLoginRoleAsync(string attributes)
    {
        var name = $"role_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(SuperuserConnectionString);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE ROLE {name} LOGIN {attributes} PASSWORD '{Password}'", connection);
        await create.ExecuteNonQueryAsync();

        return ConnectionStringFor(name);
    }

    // A database with the roles but no migrations, as a new environment is before its migration step runs.
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"empty_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await create.ExecuteNonQueryAsync();
        }

        await RunBootstrapScriptAsync(name);
        return name;
    }

    public async Task<T?> ScalarAsync<T>(string sql, string? role = null, string database = MainDatabase)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor(role ?? DatabaseRoles.Application, database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? default : (T)value;
    }

    private async Task RunBootstrapScriptAsync(string database)
    {
        var result = await _container.ExecAsync(
        [
            "psql", "--username", "postgres", "--dbname", database,
            "-v", $"owner_password={Password}", "-v", $"application_password={Password}", "-v", $"reporting_password={Password}",
            "--file", "/tmp/bootstrap.sql",
        ]);

        result.ExitCode.ShouldBe(0, result.Stderr);
    }

    private async Task CreateProbesAsOwnerAsync()
    {
        await using (var probes = new ProbeDbContext(
            TenancyServiceCollectionExtensions.ModuleDbContextOptions<ProbeDbContext>(ProbeDbContext.Schema, OwnerConnectionString),
            new TenantContext()))
        {
            await probes.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();
        await using var grant = new NpgsqlCommand(
            $"""
            GRANT USAGE ON SCHEMA {ProbeDbContext.Schema} TO {DatabaseRoles.Application};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {ProbeDbContext.Schema} TO {DatabaseRoles.Application};
            """,
            connection);
        await grant.ExecuteNonQueryAsync();
    }
}
