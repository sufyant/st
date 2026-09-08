using System.Net;
using System.Net.Http.Headers;
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantResolutionMiddlewareTests : IDisposable
{
    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;

    public TenantResolutionMiddlewareTests(PostgresContainerFixture fixture)
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

    [Fact]
    public async Task GetWhoAmI_UnknownTenant_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken("clerk_unknown_tenant_test");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/does-not-exist-{Guid.NewGuid():N}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_AuthenticatedNoMembership_ReturnsForbidden()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"noaccess-{Guid.NewGuid():N}"[..20]), "No Access Tenant");

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken($"clerk_no_membership_{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_AuthenticatedWithMembership_ReturnsOkWithTenantClaims()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"hasaccess-{Guid.NewGuid():N}"[..20]), "Has Access Tenant");

        var clerkUserId = $"clerk_has_membership_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        var membership = Membership.Create(user.Id, tenant.Id, "member");
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        // "member" must also be granted the "tenant.whoami" permission for this
        // request to succeed now that the endpoint enforces PermissionAuthorizationHandler.
        // Check-then-add: RolePermission.(Role, Permission) has a unique index and
        // PermissionAuthorizationTests seeds this same pair.
        var hasPermission = await dbContext.RolePermissions
            .AnyAsync(rp => rp.Role == "member" && rp.Permission == "tenant.whoami");
        if (!hasPermission)
        {
            dbContext.RolePermissions.Add(RolePermission.Create("member", "tenant.whoami"));
            await dbContext.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
