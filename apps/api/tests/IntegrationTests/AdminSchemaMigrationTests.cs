using Infrastructure.Persistence.Admin;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class AdminSchemaMigrationTests
{
    [Fact]
    public async Task Migrate_CreatesControlPlaneTablesInAdminSchema()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "admin"))
            .Options;
        await using var context = new AdminDbContext(options);

        // Act
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass('admin.tenants')::text", connection);
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        var tableName = result is DBNull ? null : (string)result!;
        await using var membershipCommand = new NpgsqlCommand("SELECT to_regclass('admin.memberships')::text", connection);
        var membershipResult = await membershipCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        var membershipTableName = membershipResult is DBNull ? null : (string)membershipResult!;
        await using var columnsCommand = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'admin' AND table_name = 'memberships'",
            connection);
        await using var reader = await columnsCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var membershipColumns = new List<string>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            membershipColumns.Add(reader.GetString(0));
        }

        // Assert
        Assert.Equal("admin.tenants", tableName);
        Assert.Equal("admin.memberships", membershipTableName);
        Assert.Contains("external_user_id", membershipColumns);
    }
}
