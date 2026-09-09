using Infrastructure.Persistence.Tenants;
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
