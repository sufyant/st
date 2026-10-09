using Api.Persistence;
using Audit.Infrastructure;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Infrastructure;
using Npgsql;
using Tenancy;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Api.IntegrationTests.Database))]

namespace Api.IntegrationTests;

// One PostgreSQL 18 server for the test assembly. Its main database is set up the way a deployment is: the bootstrap
// script, then every module's migrations and the message storage as the owner. The probe tables stand in for a
// module's tenant entity.
public sealed class Database : IAsyncLifetime
{
    private const string Password = "test-password";
    private const string MainDatabase = "api";

    // The tests run the application in many hosts at once, each with its own pools for requests and messages; together
    // they need more connections than PostgreSQL's default of 100.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18")
        .WithDatabase(MainDatabase)
        .WithCommand("-c", "max_connections=300")
        .Build();

    private readonly SemaphoreSlim _bootstrap = new(1, 1);

    public string ApplicationConnectionString => ConnectionStringFor(DatabaseRoles.Application);

    public string OwnerConnectionString => ConnectionStringFor(DatabaseRoles.Owner);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await _container.CopyAsync(await File.ReadAllBytesAsync("bootstrap.sql"), "/tmp/bootstrap.sql");
        await RunBootstrapScriptAsync(MainDatabase);

        await MigrateAsync(MainDatabase);
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

    // A database set up the way a deployment is, for a test that must not disturb the main one.
    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var name = await CreateEmptyDatabaseAsync();
        await MigrateAsync(name);
        return name;
    }

    // As when the database goes away under a running application: nobody can connect any more, and open sessions end.
    public async Task CloseAsync(string database)
    {
        await using var connection = new NpgsqlConnection(SuperuserConnectionString);
        await connection.OpenAsync();
        await using var close = new NpgsqlCommand(
            $"""
            ALTER DATABASE {database} ALLOW_CONNECTIONS false;
            SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{database}';
            """,
            connection);
        await close.ExecuteNonQueryAsync();
    }

    // Whether another transaction holds a lock on the rows the query selects FOR UPDATE NOWAIT in the tenant.
    public async Task<bool> IsLockedAsync(Guid tenantId, string selectForUpdateNoWait)
    {
        await using var connection = new NpgsqlConnection(ApplicationConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await DeclareTenantAsync(connection, transaction, tenantId);
        await using var command = new NpgsqlCommand(selectForUpdateNoWait, connection, transaction);
        try
        {
            await command.ExecuteNonQueryAsync();
            return false;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return true;
        }
    }

    public string SuperuserConnectionString =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = MainDatabase }.ConnectionString;

    // A role the bootstrap script never creates, for proving that the application refuses to run as one. It holds the application
    // role's privileges, so only its attributes set it apart.
    public async Task<string> CreateLoginRoleAsync(string attributes)
    {
        var name = $"role_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(SuperuserConnectionString);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE ROLE {name} LOGIN {attributes} PASSWORD '{Password}' IN ROLE {DatabaseRoles.Application}", connection);
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

    // What is really in a table: the container's superuser is bound by no policy, so it sees the rows of every tenant.
    public async Task<T?> ScalarAsSuperuserAsync<T>(string sql, string database = MainDatabase)
    {
        await using var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Database = database }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? default : (T)value;
    }

    // Runs the SQL as the role in a transaction that declares the tenant first, the way the application does; returns the rows
    // it changed.
    public async Task<int> ExecuteInTenantAsync(Guid tenantId, string sql, string? role = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor(role ?? DatabaseRoles.Application));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await DeclareTenantAsync(connection, transaction, tenantId);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var changed = await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        return changed;
    }

    public async Task<T?> ScalarInTenantAsync<T>(Guid tenantId, string sql, string? role = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor(role ?? DatabaseRoles.Application));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await DeclareTenantAsync(connection, transaction, tenantId);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? default : (T)value;
    }

    // Runs the SQL as the role in a transaction that declares the catalog user first, as GET /v1/me/tenants does (R11); returns the
    // rows it changed.
    public async Task<int> ExecuteAsUserAsync(Guid userId, string sql)
    {
        await using var connection = new NpgsqlConnection(ApplicationConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await DeclareUserAsync(connection, transaction, userId);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var changed = await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        return changed;
    }

    public async Task<T?> ScalarAsUserAsync<T>(Guid userId, string sql)
    {
        await using var connection = new NpgsqlConnection(ApplicationConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await DeclareUserAsync(connection, transaction, userId);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? default : (T)value;
    }

    private static async Task DeclareUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId)
    {
        await using var declare = new NpgsqlCommand("SELECT set_config('app.user_id', @user, true)", connection, transaction);
        declare.Parameters.AddWithValue("user", userId.ToString());
        await declare.ExecuteNonQueryAsync();
    }

    private static async Task DeclareTenantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId)
    {
        await using var declare = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction);
        declare.Parameters.AddWithValue("tenant", tenantId.ToString());
        await declare.ExecuteNonQueryAsync();
    }

    private async Task MigrateAsync(string database)
    {
        await using var services = new ServiceCollection()
            .AddTenancy(_ => ConnectionStringFor(DatabaseRoles.Application, database))
            .AddControlPlaneInfrastructure()
            .AddNotificationsInfrastructure()
            .AddAuditInfrastructure()
            .BuildServiceProvider();

        await MigrationStep.RunAsync(services, ConnectionStringFor(DatabaseRoles.Owner, database), CancellationToken.None);
    }

    // One run at a time: the script alters the shared roles, and two ALTER ROLE at once fail with "tuple concurrently updated".
    // Tests that need a database of their own create them in parallel.
    private async Task RunBootstrapScriptAsync(string database)
    {
        await _bootstrap.WaitAsync();
        try
        {
            var result = await _container.ExecAsync(
            [
                "psql", "--username", "postgres", "--dbname", database,
                "-v", $"owner_password={Password}", "-v", $"application_password={Password}",
                "--file", "/tmp/bootstrap.sql",
            ]);

            result.ExitCode.ShouldBe(0, result.Stderr);
        }
        finally
        {
            _bootstrap.Release();
        }
    }

    private async Task CreateProbesAsOwnerAsync()
    {
        await using (var probes = new ProbeDbContext(
            TenancyServiceCollectionExtensions.ModuleDbContextOptions<ProbeDbContext>(ProbeDbContext.Schema, OwnerConnectionString)))
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
