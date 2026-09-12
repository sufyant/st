using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Shared;
using Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class MemberEndpointTests
{
    private const string InvitedUserId = "user_invited";
    private const string InvitedEmail = "invited@example.com";

    private static string MembersUrl => $"/{TenantSurfaceFixture.Alias}/api/v1/members";

    [Fact]
    public async Task GetMembers_ListsUsersWithTheirRoles()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.GetAsync(MembersUrl, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        var only = Assert.Single(body!);
        Assert.Equal(TenantSurfaceFixture.OwnerUserId, only.GetProperty("externalUserId").GetString());
        Assert.Equal("Active", only.GetProperty("status").GetString());
        Assert.Equal("owner", only.GetProperty("roleCode").GetString());
    }

    [Fact]
    public async Task GetMembers_ReturnsTheEmailOfEachMember()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.GetAsync(MembersUrl, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement[]>(TestContext.Current.CancellationToken);
        var only = Assert.Single(body!);
        Assert.Equal(TenantSurfaceFixture.OwnerEmail, only.GetProperty("email").GetString());
    }

    [Fact]
    public async Task DeleteMember_RevokesEntryAndDisablesTheUser()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();

        // Act
        using var response = await fixture.Client.DeleteAsync(
            $"{MembersUrl}/{InvitedUserId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var invitedUserId = ExternalUserId.Create(InvitedUserId);
        await using var control = fixture.CreateControlPlane();
        Assert.False(await control.Memberships.AnyAsync(
            membership => membership.ExternalUserId == invitedUserId,
            TestContext.Current.CancellationToken));
        await using var tenantDbContext = fixture.CreateTenantDbContext();
        var user = await tenantDbContext.Users
            .Include(candidate => candidate.Role)
            .SingleAsync(candidate => candidate.ExternalUserId == invitedUserId, TestContext.Current.CancellationToken);
        Assert.Equal(UserStatus.Disabled, user.Status);
        Assert.Null(user.Role);
    }

    [Fact]
    public async Task DeleteMember_AfterRevocation_TheTenantRejectsThem()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();
        using var revoked = await fixture.Client.DeleteAsync(
            $"{MembersUrl}/{InvitedUserId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        await using var invited = fixture.WithPrincipal(InvitedUserId, InvitedEmail);

        // Act
        using var response = await invited.Client.GetAsync(MembersUrl, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMember_ForTheLastOwner_ReturnsNoContent()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.DeleteAsync(
            $"{MembersUrl}/{TenantSurfaceFixture.OwnerUserId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DisableMember_ForTheLastOwner_ReturnsNoContent()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PostAsync(
            $"{MembersUrl}/{TenantSurfaceFixture.OwnerUserId}/disable",
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PutRoles_ReplacesTheRolesAndChangesWhatTheMemberMayDo()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();
        await using var invited = fixture.WithPrincipal(InvitedUserId, InvitedEmail);
        using var beforeUpgrade = await invited.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email = "third@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, beforeUpgrade.StatusCode);

        // Act
        using var upgraded = await fixture.Client.PutAsJsonAsync(
            $"{MembersUrl}/{InvitedUserId}/role",
            new { roleCode = "owner" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, upgraded.StatusCode);
        using var afterUpgrade = await invited.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email = "third@example.com", roleCode = "member" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, afterUpgrade.StatusCode);
    }

    [Fact]
    public async Task PutRoles_ThatDropsTheLastOwner_ReturnsNoContent()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.PutAsJsonAsync(
            $"{MembersUrl}/{TenantSurfaceFixture.OwnerUserId}/role",
            new { roleCode = "member" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PutRoles_ForAnUnknownRole_ReturnsBadRequest()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();

        // Act
        using var response = await fixture.Client.PutAsJsonAsync(
            $"{MembersUrl}/{InvitedUserId}/role",
            new { roleCode = "sorcerer" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisableThenEnable_RestoresAccess()
    {
        // Arrange
        await using var fixture = await AcceptedMemberAsync();
        await using var invited = fixture.WithPrincipal(InvitedUserId, InvitedEmail);

        // Act
        using var disabled = await fixture.Client.PostAsync(
            $"{MembersUrl}/{InvitedUserId}/disable",
            content: null,
            TestContext.Current.CancellationToken);
        using var whileDisabled = await invited.Client.GetAsync(
            MembersUrl,
            TestContext.Current.CancellationToken);
        using var enabled = await fixture.Client.PostAsync(
            $"{MembersUrl}/{InvitedUserId}/enable",
            content: null,
            TestContext.Current.CancellationToken);
        using var afterEnable = await invited.Client.GetAsync(
            MembersUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, whileDisabled.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, enabled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterEnable.StatusCode);
    }

    [Fact]
    public async Task DeleteMember_ForAnUnknownUser_ReturnsNotFound()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();

        // Act
        using var response = await fixture.Client.DeleteAsync(
            $"{MembersUrl}/user_nobody",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<TenantSurfaceFixture> AcceptedMemberAsync()
    {
        var fixture = await TenantSurfaceFixture.StartAsync();
        using var created = await fixture.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email = InvitedEmail, roleCode = "member" },
            TestContext.Current.CancellationToken);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var token = body.GetProperty("token").GetString()!;
        await using var invited = fixture.WithPrincipal(InvitedUserId, InvitedEmail);
        using var accepted = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        return fixture;
    }
}
