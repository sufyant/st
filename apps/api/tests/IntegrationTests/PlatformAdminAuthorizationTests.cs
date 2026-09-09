using System.Net;
using Xunit;

namespace IntegrationTests;

public sealed class PlatformAdminAuthorizationTests
{
    [Fact]
    public async Task GetTenants_ForANonPlatformAdmin_ReturnsForbidden()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: false);

        // Act
        using var response = await fixture.Client.GetAsync(
            "/admin/api/v1/tenants",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTenants_ForAPlatformAdmin_ReturnsOk()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: true);

        // Act
        using var response = await fixture.Client.GetAsync(
            "/admin/api/v1/tenants",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
