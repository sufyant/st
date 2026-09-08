using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class WhoAmIEndpointTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    public WhoAmIEndpointTests(PostgresContainerFixture fixture)
    {
        _factory = new CustomWebApplicationFactory(fixture.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetWhoAmI_NoToken_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_ValidToken_ReturnsSubClaim()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken("clerk_test_user_1");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhoAmIResponse>();
        Assert.Equal("clerk_test_user_1", body?.UserId);
    }

    [Fact]
    public async Task GetWhoAmI_ExpiredToken_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken("clerk_test_user_1", lifetime: TimeSpan.FromMinutes(-5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_WrongSigningKey_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        using var wrongKey = RSA.Create(2048);
        var token = TestJwtTokenFactory.CreateToken(
            "clerk_test_user_1", signingKey: new RsaSecurityKey(wrongKey));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record WhoAmIResponse(string UserId);
}
