using Infrastructure.Persistence.Admin;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class AdminSchemaMigrationTests
{
    [Fact]
    public async Task Migrate_CreatesTenantsTableInAdminSchema()
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

        // Assert
        Assert.Equal("admin.tenants", tableName);
    }
}
