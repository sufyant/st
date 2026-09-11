using Domain.ControlPlane.Tenants;
using Infrastructure.Provisioning;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantProvisionerTests
{
    private static readonly TenantDatabaseName DatabaseName = TenantDatabaseName.Create("tenant_acme");

    [Fact]
    public async Task CreateDatabaseAsync_CreatesTheDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString());

        // Act
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await DatabaseExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    }

    [Fact]
    public async Task CreateDatabaseAsync_RunTwice_Succeeds()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString());
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Act
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await DatabaseExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    }

    [Fact]
    public async Task MigrateSchemaAsync_CreatesTheTenantTables()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString());
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Act
        await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await UsersTableExistsAsync(postgres.GetConnectionString(), DatabaseName.Value));
    }

    [Fact]
    public async Task GrantTenantAccessAsync_RunTwice_LetsTheTenantRoleReadTheTables()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(postgres.GetConnectionString(), "CREATE ROLE st_tenant LOGIN PASSWORD 'test'");
        var provisioner = new TenantProvisioner(postgres.GetConnectionString());
        await provisioner.CreateDatabaseAsync(DatabaseName, TestContext.Current.CancellationToken);
        await provisioner.MigrateSchemaAsync(DatabaseName, TestContext.Current.CancellationToken);
        await provisioner.GrantTenantAccessAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Act
        await provisioner.GrantTenantAccessAsync(DatabaseName, TestContext.Current.CancellationToken);

        // Assert
        var tenantConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = DatabaseName.Value,
            Username = "st_tenant",
            Password = "test"
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(tenantConnection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM users", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<bool> DatabaseExistsAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", connection);
        command.Parameters.AddWithValue("name", databaseName);

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<bool> UsersTableExistsAsync(string connectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.users')::text", connection);

        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) is "users";
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
