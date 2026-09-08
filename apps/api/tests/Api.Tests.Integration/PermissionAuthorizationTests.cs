using System.Net;
using System.Net.Http.Headers;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class PermissionAuthorizationTests : IDisposable
{
    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;

    public PermissionAuthorizationTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
        _factory = new CustomWebApplicationFactory(fixture.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private AdminDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new AdminDbContext(options);
    }

    private async Task<(Tenant Tenant, string ClerkUserId)> SeedTenantAndMembershipAsync(string role)
    {
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"perm-{Guid.NewGuid():N}"[..15]), "Permission Test Tenant");

        var clerkUserId = $"clerk_perm_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        var membership = Membership.Create(user.Id, tenant.Id, role);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        return (tenant, clerkUserId);
    }

    // RolePermission.(Role, Permission) has a unique index, and different tests
    // seed the same literal ("member", "tenant.whoami") pair. Check-then-add
    // avoids a duplicate-key violation if that pair has already been seeded
    // by another test in this collection.
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
    public async Task GetWhoAmI_RoleWithoutPermission_ReturnsForbidden()
    {
        // Arrange
        var (tenant, clerkUserId) = await SeedTenantAndMembershipAsync("guest");
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_RoleWithPermission_ReturnsOk()
    {
        // Arrange
        var (tenant, clerkUserId) = await SeedTenantAndMembershipAsync("member");
        await EnsureRolePermissionAsync("member", "tenant.whoami");

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
