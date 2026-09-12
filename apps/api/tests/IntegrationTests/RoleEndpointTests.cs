using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IntegrationTests;

public sealed class RoleEndpointTests
{
    private static string RolesUrl => $"/{TenantSurfaceFixture.Alias}/api/v1/roles";

    [Fact]
    public async Task GetRoles_ListsSystemRolesWithTheirPermissions()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.GetAsync(RolesUrl, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        var codes = body!.Select(role => role.GetProperty("code").GetString()).ToArray();
        Assert.Equal(new[] { "member", "owner" }, codes);
        var owner = body.Single(role => role.GetProperty("code").GetString() == "owner");
        Assert.Equal(5, owner.GetProperty("permissionCodes").GetArrayLength());
        var member = body.Single(role => role.GetProperty("code").GetString() == "member");
        Assert.Equal(
            new[] { "members.read", "roles.read" },
            member.GetProperty("permissionCodes").EnumerateArray().Select(code => code.GetString()).ToArray());
    }

    [Fact]
    public async Task GetRoles_ForAMember_IsAllowed()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync(
            "user_member",
            "member@example.com",
            "member");

        // Act
        using var response = await fixture.Client.GetAsync(RolesUrl, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
