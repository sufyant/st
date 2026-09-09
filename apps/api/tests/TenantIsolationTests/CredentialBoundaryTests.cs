using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace TenantIsolationTests;

public sealed class CredentialBoundaryTests
{
    [Fact]
    public async Task TenantRole_CannotWriteToTheControlPlane()
    {
        // Arrange
        await using var postgres = await StartControlPlaneAsync();
        await using var connection = new NpgsqlConnection(TenantConnectionString(postgres));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO control.tenants (id, alias, database_name, status, created_at, updated_at) " +
            "VALUES (gen_random_uuid(), 'intruder', 'tenant_intruder', 'Active', now(), now())",
            connection);

        // Act
        var act = async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(act);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task TenantRole_CannotRunSchemaChanges()
    {
        // Arrange
        await using var postgres = await StartControlPlaneAsync();
        await using var connection = new NpgsqlConnection(TenantConnectionString(postgres));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("CREATE TABLE control.intruder (id uuid)", connection);

        // Act
        var act = async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(act);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task TenantRole_CanReadTheTenantCatalogue()
    {
        // Arrange
        await using var postgres = await StartControlPlaneAsync();
        await using var connection = new NpgsqlConnection(TenantConnectionString(postgres));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM control.tenants", connection);

        // Act
        var count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0L, count);
    }

    private static async Task<PostgreSqlContainer> StartControlPlaneAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();

        await ExecuteAsync(connectionString, await ReadScriptAsync("bootstrap-roles.sql"));
        await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");

        var controlPlane = WithDatabase(connectionString, "control_plane");
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(controlPlane, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;
        await using (var context = new ControlPlaneDbContext(options))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await ExecuteAsync(controlPlane, await ReadScriptAsync("grant-control-plane.sql"));

        return postgres;
    }

    private static async Task<string> ReadScriptAsync(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", fileName));
        var sql = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        return sql
            .Replace(":'migrator_password'", "'test'", StringComparison.Ordinal)
            .Replace(":'provisioner_password'", "'test'", StringComparison.Ordinal)
            .Replace(":'control_password'", "'test'", StringComparison.Ordinal)
            .Replace(":'tenant_password'", "'test'", StringComparison.Ordinal);
    }

    private static string TenantConnectionString(PostgreSqlContainer postgres) =>
        new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = "control_plane",
            Username = "st_tenant",
            Password = "test"
        }.ConnectionString;

    private static string WithDatabase(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
