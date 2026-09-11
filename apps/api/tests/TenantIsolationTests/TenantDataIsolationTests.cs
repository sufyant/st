using Infrastructure.Persistence;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace TenantIsolationTests;

public sealed class TenantDataIsolationTests
{
    [Fact]
    public async Task TenantData_CannotBeReadFromAnotherTenantDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        var contextFactory = new TenantDbContextFactory(connectionString, new AuditInterceptor(TimeProvider.System));
        var migrator = new TenantSchemaMigrator(contextFactory);
        await ExecuteAsync(connectionString, "CREATE DATABASE tenant_acme");
        await ExecuteAsync(connectionString, "CREATE DATABASE tenant_globex");
        await migrator.MigrateAsync("tenant_acme", TestContext.Current.CancellationToken);
        await migrator.MigrateAsync("tenant_globex", TestContext.Current.CancellationToken);
        await using var acme = contextFactory.Create("tenant_acme");
        await using var globex = contextFactory.Create("tenant_globex");
        var now = DateTimeOffset.UtcNow;
        await acme.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO users (id, external_user_id, email, status, created_at, updated_at)
            VALUES ({Guid.NewGuid()}, {"user_2abc123"}, {"user@example.com"}, {"Active"}, {now}, {now})
            """,
            TestContext.Current.CancellationToken);

        // Act
        var acmeUsers = await acme.Users.ToListAsync(TestContext.Current.CancellationToken);
        var globexUsers = await globex.Users.ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("user_2abc123", Assert.Single(acmeUsers).ExternalUserId.Value);
        Assert.Empty(globexUsers);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
