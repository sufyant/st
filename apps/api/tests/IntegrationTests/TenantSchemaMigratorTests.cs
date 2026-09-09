using Domain.Access;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantSchemaMigratorTests
{
    [Fact]
    public async Task MigrateAsync_ForAnExistingDatabase_CreatesTheTenantSchema()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE DATABASE tenant_acme");
        var migrator = new TenantSchemaMigrator(new TenantDbContextFactory(connectionString));

        // Act
        await migrator.MigrateAsync("tenant_acme", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await HasUsersTableAsync(connectionString, "tenant_acme"));
    }

    [Fact]
    public async Task MigrateAsync_ForAMissingDatabase_ThrowsAndDoesNotCreateIt()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        var migrator = new TenantSchemaMigrator(new TenantDbContextFactory(connectionString));

        // Act
        var act = async () => await migrator.MigrateAsync("tenant_missing", TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.False(await DatabaseExistsAsync(connectionString, "tenant_missing"));
    }

    [Fact]
    public async Task MigrateAsync_SeedsTheSystemAccessCatalog()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE DATABASE tenant_acme");
        var migrator = new TenantSchemaMigrator(new TenantDbContextFactory(connectionString));

        // Act
        await migrator.MigrateAsync("tenant_acme", TestContext.Current.CancellationToken);

        // Assert
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "tenant_acme" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM permissions), (SELECT count(*) FROM roles), " +
            "(SELECT count(*) FROM role_permissions)",
            connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SystemAccessCatalog.Permissions.Count, reader.GetInt64(0));
        Assert.Equal(SystemAccessCatalog.Roles.Count, reader.GetInt64(1));
        Assert.Equal(SystemAccessCatalog.Roles.Sum(role => role.PermissionIds.Count), reader.GetInt64(2));
    }

    [Fact]
    public async Task MigrateAsync_ConvergesRegardlessOfTheStartingVersion()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE DATABASE tenant_upgraded");
        await ExecuteAsync(connectionString, "CREATE DATABASE tenant_fresh");
        var contextFactory = new TenantDbContextFactory(connectionString);

        await using (var stepped = contextFactory.Create("tenant_upgraded"))
        {
            await stepped.GetService<IMigrator>()
                .MigrateAsync("InitialTenantAccess", TestContext.Current.CancellationToken);
        }

        var migrator = new TenantSchemaMigrator(contextFactory);

        // Act
        await migrator.MigrateAsync("tenant_upgraded", TestContext.Current.CancellationToken);
        await migrator.MigrateAsync("tenant_fresh", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            await AppliedMigrationsAsync(connectionString, "tenant_fresh"),
            await AppliedMigrationsAsync(connectionString, "tenant_upgraded"));
    }

    private static async Task<List<string>> AppliedMigrationsAsync(string connectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"",
            connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var migrationIds = new List<string>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            migrationIds.Add(reader.GetString(0));
        }

        return migrationIds;
    }

    internal static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    internal static async Task<bool> DatabaseExistsAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", connection);
        command.Parameters.AddWithValue("name", databaseName);

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<bool> HasUsersTableAsync(string connectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.users')::text", connection);

        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) is "users";
    }
}
