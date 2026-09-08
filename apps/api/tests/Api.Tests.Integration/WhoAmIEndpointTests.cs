using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class WhoAmIEndpointTests : IDisposable
{
    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;

    public WhoAmIEndpointTests(PostgresContainerFixture fixture)
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

    private async Task<Tenant> ProvisionTenantAsync()
    {
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        return await provisioningService.ProvisionAsync(
            TenantSlug.Create($"whoami-{Guid.NewGuid():N}"[..20]), "WhoAmI Test Tenant");
    }

    [Fact]
    public async Task GetWhoAmI_NoToken_ReturnsUnauthorized()
    {
        // Arrange
        var tenant = await ProvisionTenantAsync();
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_ValidToken_ReturnsForbiddenWithoutMembership()
    {
        // Arrange
        var tenant = await ProvisionTenantAsync();
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken("clerk_test_user_1");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert: proves the token was validated (not 401) - tenant resolution's
        // membership check now takes over and rejects with 403 since no
        // Membership row exists for this user in this tenant.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_ExpiredToken_ReturnsUnauthorized()
    {
        // Arrange
        var tenant = await ProvisionTenantAsync();
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken("clerk_test_user_1", lifetime: TimeSpan.FromMinutes(-5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_WrongSigningKey_ReturnsUnauthorized()
    {
        // Arrange
        var tenant = await ProvisionTenantAsync();
        var client = _factory.CreateClient();
        using var wrongKey = RSA.Create(2048);
        var token = TestJwtTokenFactory.CreateToken(
            "clerk_test_user_1", signingKey: new RsaSecurityKey(wrongKey));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
