using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class RenameTenantEndpointTests : IDisposable
{
    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;

    public RenameTenantEndpointTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
        _factory = new CustomWebApplicationFactory(fixture.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private AdminDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(_fixture.ConnectionString).Options;
        return new AdminDbContext(options);
    }

    private async Task EnsureRolePermissionAsync(string role, string permission)
    {
        await using var dbContext = CreateDbContext();
        var exists = await dbContext.RolePermissions
            .AnyAsync(rp => rp.Role == role && rp.Permission == permission);
        if (exists)
        {
            return;
        }

        dbContext.RolePermissions.Add(RolePermission.Create(role, permission));
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task PutRenameTenant_ValidRequestWithPermission_RenamesAndReturns204()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"api-{Guid.NewGuid():N}"[..15]), "Original Name");

        var clerkUserId = $"clerk_api_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenant.Id, "owner"));
        await dbContext.SaveChangesAsync();

        await EnsureRolePermissionAsync("owner", "tenant.rename");

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PutAsJsonAsync(
            $"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "Renamed via HTTP" });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Renamed via HTTP", reloaded.Name);
    }

    [Fact]
    public async Task PutRenameTenant_NoToken_Returns401()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"api-{Guid.NewGuid():N}"[..15]), "Original Name");
        var client = _factory.CreateClient();

        // Act
        var response = await client.PutAsJsonAsync(
            $"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "Should Not Apply" });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutRenameTenant_NoPermission_Returns403AndDoesNotPersist()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"api-{Guid.NewGuid():N}"[..15]), "Original Name");

        var clerkUserId = $"clerk_api_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenant.Id, "guest"));
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PutAsJsonAsync(
            $"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "Should Not Apply" });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Original Name", reloaded.Name);
    }

    [Fact]
    public async Task PutRenameTenant_EmptyName_Returns400()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"api-{Guid.NewGuid():N}"[..15]), "Original Name");

        var clerkUserId = $"clerk_api_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenant.Id, "owner"));
        await dbContext.SaveChangesAsync();

        await EnsureRolePermissionAsync("owner", "tenant.rename");

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PutAsJsonAsync(
            $"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
