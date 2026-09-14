using Domain.ControlPlane.Tenants;
using Infrastructure.Tenants;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantCredentialResolverTests
{
    [Fact]
    public async Task ResolveAsync_DecryptsTheStoredPassword()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var dataProtectionProvider = DataProtectionProvider.Create("tests");
        var protector = dataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);
        var tenantId = Guid.CreateVersion7();
        await SeedCredentialAsync(
            postgres.GetConnectionString(), tenantId, "access_acme", protector.Protect("s3cr3t"));
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantCredentialResolver(dataSource, dataProtectionProvider, cache);

        // Act
        var resolved = await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal("access_acme", resolved.RoleName);
        Assert.Equal("s3cr3t", resolved.Password);
    }

    [Fact]
    public async Task ResolveAsync_ServesTheSecondCallFromCache()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var dataProtectionProvider = DataProtectionProvider.Create("tests");
        var protector = dataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);
        var tenantId = Guid.CreateVersion7();
        await SeedCredentialAsync(
            postgres.GetConnectionString(), tenantId, "access_acme", protector.Protect("s3cr3t"));
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantCredentialResolver(dataSource, dataProtectionProvider, cache);
        await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);
        await ExecuteAsync(postgres.GetConnectionString(), "DELETE FROM control.tenant_credentials");

        // Act
        var cached = await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(cached);
    }

    [Fact]
    public async Task Evict_ForcesTheNextCallToReadTheDatabase()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var dataProtectionProvider = DataProtectionProvider.Create("tests");
        var protector = dataProtectionProvider.CreateProtector(
            TenantCredential.ProtectionPurpose);
        var tenantId = Guid.CreateVersion7();
        await SeedCredentialAsync(
            postgres.GetConnectionString(), tenantId, "access_acme", protector.Protect("s3cr3t"));
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantCredentialResolver(dataSource, dataProtectionProvider, cache);
        await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);
        await ExecuteAsync(postgres.GetConnectionString(), "DELETE FROM control.tenant_credentials");

        // Act
        resolver.Evict(tenantId);
        var resolved = await resolver.ResolveAsync(tenantId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(resolved);
    }

    private static async Task SeedCredentialAsync(
        string connectionString, Guid tenantId, string roleName, string encryptedPassword)
    {
        await ExecuteAsync(connectionString, "CREATE SCHEMA control");
        await ExecuteAsync(
            connectionString,
            "CREATE TABLE control.tenant_credentials (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, " +
            "role_name text NOT NULL, encrypted_password text NOT NULL)");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO control.tenant_credentials VALUES (gen_random_uuid(), @tenantId, @roleName, @password)",
            connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("roleName", roleName);
        command.Parameters.AddWithValue("password", encryptedPassword);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
