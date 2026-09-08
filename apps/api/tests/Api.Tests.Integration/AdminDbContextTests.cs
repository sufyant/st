using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class AdminDbContextTests(PostgresContainerFixture fixture)
{
    private AdminDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        return new AdminDbContext(options);
    }

    [Fact]
    public async Task Migration_CreatesAllFiveAdminTables()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        // Act
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'admin' ORDER BY table_name",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tableNames = new List<string>();
        while (await reader.ReadAsync())
        {
            tableNames.Add(reader.GetString(0));
        }

        // Assert
        Assert.Contains("Tenants", tableNames);
        Assert.Contains("Users", tableNames);
        Assert.Contains("Memberships", tableNames);
        Assert.Contains("Invitations", tableNames);
        Assert.Contains("RolePermissions", tableNames);
    }

    [Fact]
    public async Task Migration_PutsEFMigrationsHistoryTableInAdminSchema()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        // Act
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'admin' AND table_name = '__EFMigrationsHistory'",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var found = await reader.ReadAsync();

        // Assert
        Assert.True(found, "__EFMigrationsHistory should live in the admin schema, not public.");
    }

    [Fact]
    public async Task AddTenant_PersistsAndReloads()
    {
        // Arrange
        var slug = Api.Domain.TenantSlug.Create($"test-{Guid.NewGuid():N}"[..20]);
        var tenant = Api.Domain.Tenant.Create(slug, "Test Tenant");

        // Act
        await using (var writeContext = CreateContext())
        {
            writeContext.Tenants.Add(tenant);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext();
        var reloaded = await readContext.Tenants.SingleAsync(t => t.Id == tenant.Id);

        // Assert
        Assert.Equal(slug, reloaded.Slug);
        Assert.Equal("Test Tenant", reloaded.Name);
    }
}
