using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Access;
using Domain.Access.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class InvitationAcceptanceTests
{
    private const string InvitedUserId = "user_invited";
    private const string InvitedEmail = "invited@example.com";

    [Fact]
    public async Task AcceptInvitation_LetsTheInvitedPersonUseTheTenant()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");
        await using var invited = owner.WithPrincipal(InvitedUserId, InvitedEmail);

        // Act
        using var accepted = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var members = await invited.Client.GetAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/members",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        await using var tenantDbContext = owner.CreateTenantDbContext();
        var invitedUserId = ExternalUserId.Create(InvitedUserId);
        var user = await tenantDbContext.Users.SingleAsync(
            candidate => candidate.ExternalUserId == invitedUserId,
            TestContext.Current.CancellationToken);
        Assert.Equal(TenantUserStatus.Active, user.Status);
    }

    [Fact]
    public async Task AcceptInvitation_WithAnUnknownToken_ReturnsNotFound()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync(
            InvitedUserId,
            InvitedEmail,
            roleCode: null);

        // Act
        using var response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token = "not-a-real-token" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_WithAMismatchedEmail_ReturnsForbidden()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");
        await using var stranger = owner.WithPrincipal("user_stranger", "stranger@example.com");

        // Act
        using var response = await stranger.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_WithoutAnEmailClaim_ReturnsForbidden()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");
        await using var anonymousEmail = owner.WithPrincipal(InvitedUserId, email: null);

        // Act
        using var response = await anonymousEmail.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_Twice_ReturnsNotFound()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");
        await using var invited = owner.WithPrincipal(InvitedUserId, InvitedEmail);
        using var first = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Act
        using var response = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_AfterItExpired_ReturnsGone()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        var token = await InviteAsync(owner, InvitedEmail, "member");

        await using (var context = owner.CreateControlPlane())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE control.invitations SET expires_at = now() - interval '1 day'",
                TestContext.Current.CancellationToken);
        }

        await using var invited = owner.WithPrincipal(InvitedUserId, InvitedEmail);

        // Act
        using var response = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task GetMyInvitations_ListsOnlyMyPendingOnes()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        await InviteAsync(owner, InvitedEmail, "member");
        await InviteAsync(owner, "someone.else@example.com", "member");
        await using var invited = owner.WithPrincipal(InvitedUserId, InvitedEmail);

        // Act
        using var response = await invited.Client.GetAsync(
            "/api/v1/invitations",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        var only = Assert.Single(body!);
        Assert.Equal(InvitedEmail, only.GetProperty("email").GetString());
        Assert.Equal(TenantSurfaceFixture.Alias, only.GetProperty("tenantAlias").GetString());
    }

    [Fact]
    public async Task GetMyMemberships_ListsOnlyTenantsIBelongTo()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        await using var stranger = owner.WithPrincipal("user_stranger", "stranger@example.com");

        // Act
        using var mine = await owner.Client.GetAsync("/api/v1/memberships", TestContext.Current.CancellationToken);
        using var theirs = await stranger.Client.GetAsync(
            "/api/v1/memberships",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single((await mine.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken))!);
        Assert.Empty((await theirs.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken))!);
    }

    private static async Task<string> InviteAsync(
        TenantSurfaceFixture fixture,
        string email,
        string roleCode)
    {
        using var response = await fixture.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email, roleCode },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        return body.GetProperty("token").GetString()!;
    }
}
