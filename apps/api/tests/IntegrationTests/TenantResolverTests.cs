using Infrastructure.Tenants;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantResolverTests
{
    [Fact]
    public async Task ResolveAsync_ServesTheSecondCallFromCache()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await SeedTenantAsync(postgres.GetConnectionString(), "acme");
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantResolver(dataSource, cache);
        await resolver.ResolveAsync("acme", TestContext.Current.CancellationToken);
        await ExecuteAsync(postgres.GetConnectionString(), "DELETE FROM control.tenants");

        // Act
        var cached = await resolver.ResolveAsync("acme", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(cached);
        Assert.Equal("acme", cached.Alias);
    }

    [Fact]
    public async Task Evict_ForcesTheNextCallToReadTheDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await SeedTenantAsync(postgres.GetConnectionString(), "acme");
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantResolver(dataSource, cache);
        await resolver.ResolveAsync("acme", TestContext.Current.CancellationToken);
        await ExecuteAsync(postgres.GetConnectionString(), "DELETE FROM control.tenants");

        // Act
        resolver.Evict("acme");
        var resolved = await resolver.ResolveAsync("acme", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(resolved);
    }

    [Fact]
    public async Task HasMembershipAsync_IsNotCached()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await SeedTenantAsync(connectionString, "acme");
        var tenantId = Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9");
        await ExecuteAsync(
            connectionString,
            "CREATE TABLE control.memberships (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, " +
            "external_user_id text NOT NULL)");
        await ExecuteAsync(
            connectionString,
            $"INSERT INTO control.memberships VALUES (gen_random_uuid(), '{tenantId}', 'user_2abc123')");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantResolver(dataSource, cache);
        await resolver.HasMembershipAsync(tenantId, "user_2abc123", TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, "DELETE FROM control.memberships");

        // Act
        var stillMember = await resolver.HasMembershipAsync(
            tenantId,
            "user_2abc123",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(stillMember);
    }

    private static async Task SeedTenantAsync(string connectionString, string alias)
    {
        await ExecuteAsync(connectionString, "CREATE SCHEMA control");
        await ExecuteAsync(
            connectionString,
            "CREATE TABLE control.tenants (id uuid PRIMARY KEY, alias text NOT NULL, " +
            "database_name text NOT NULL, status text NOT NULL, created_at timestamptz NOT NULL, " +
            "updated_at timestamptz NOT NULL)");
        await ExecuteAsync(
            connectionString,
            "INSERT INTO control.tenants VALUES ('018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9', " +
            $"'{alias}', 'tenant_{alias}', 'Active', now(), now())");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
