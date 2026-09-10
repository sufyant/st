using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Access;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class InvitationEndpointTests
{
    private static string InvitationsUrl => $"/{TenantSurfaceFixture.Alias}/api/v1/invitations";

    [Fact]
    public async Task PostInvitation_ReturnsTheTokenOnceAndStoresOnlyItsHash()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "Invited@Example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var token = body.GetProperty("token").GetString()!;
        Assert.Equal("invited@example.com", body.GetProperty("email").GetString());
        await using var context = fixture.CreateControlPlane();
        var stored = await context.Invitations.SingleAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(token, stored.TokenHash, StringComparison.Ordinal);
        Assert.Equal(InvitationStatus.Pending, stored.Status);
        Assert.Equal(fixture.Tenant.Id, stored.TenantId);
    }

    [Fact]
    public async Task PostInvitation_ForAnUnknownRole_ReturnsBadRequest()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "invited@example.com", roleCode = "sorcerer" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostInvitation_ForAnInvalidEmail_ReturnsBadRequest()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "not-an-email", roleCode = "member" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostInvitation_ForADuplicatePendingEmail_ReturnsConflict()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        using var first = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "invited@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "INVITED@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetInvitations_ListsOnlyPendingOnes()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        using var created = await fixture.Client.PostAsJsonAsync(
            InvitationsUrl,
            new { email = "invited@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var id = body.GetProperty("id").GetGuid();

        // Act
        using var listed = await fixture.Client.GetAsync(InvitationsUrl, TestContext.Current.CancellationToken);
        using var revoked = await fixture.Client.DeleteAsync(
            $"{InvitationsUrl}/{id}",
            TestContext.Current.CancellationToken);
        using var listedAfter = await fixture.Client.GetAsync(InvitationsUrl, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Single((await listed.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken))!);
        Assert.Empty((await listedAfter.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task DeleteInvitation_ForAnUnknownIdentifier_ReturnsNotFound()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.DeleteAsync(
            $"{InvitationsUrl}/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
