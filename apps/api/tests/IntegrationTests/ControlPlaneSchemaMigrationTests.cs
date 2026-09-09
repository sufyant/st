using Infrastructure.Persistence.ControlPlane;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class ControlPlaneSchemaMigrationTests
{
    [Fact]
    public async Task Migrate_CreatesSystemDatabaseTablesInAdminSchema()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await using (var bootstrapConnection = new NpgsqlConnection(connectionString))
        {
            await bootstrapConnection.OpenAsync(TestContext.Current.CancellationToken);
            await using var bootstrapCommand = new NpgsqlCommand("CREATE DATABASE control_plane", bootstrapConnection);
            await bootstrapCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var systemDatabaseConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "control_plane"
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(systemDatabaseConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;
        await using var context = new ControlPlaneDbContext(options);

        // Act
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(systemDatabaseConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('control.tenants')::text", connection);
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        var tableName = result is DBNull ? null : (string)result!;
        await using var membershipCommand = new NpgsqlCommand("SELECT to_regclass('control.memberships')::text", connection);
        var membershipResult = await membershipCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        var membershipTableName = membershipResult is DBNull ? null : (string)membershipResult!;
        await using var columnsCommand = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'control' AND table_name = 'memberships'",
            connection);
        await using var reader = await columnsCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var membershipColumns = new List<string>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            membershipColumns.Add(reader.GetString(0));
        }

        // Assert
        Assert.Equal("control.tenants", tableName);
        Assert.Equal("control.memberships", membershipTableName);
        Assert.Contains("external_user_id", membershipColumns);
    }
}
