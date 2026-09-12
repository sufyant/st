using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Authorization;
using Domain.ControlPlane.Tenants;
using Domain.Shared;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests;

public sealed class AuditStampTests
{
    [Fact]
    public async Task SavingAnEntityStampsCreatedAndUpdated()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: false);
        await using var dbContext = fixture.CreateControlPlaneDbContext();
        var tenant = Tenant.Create(TenantAlias.Create("acme"));

        // Act
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(default, tenant.CreatedAt);
        Assert.Equal(tenant.CreatedAt, tenant.UpdatedAt);
    }

    [Fact]
    public async Task SavingAChangeMovesUpdatedButNotCreated()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: false);
        await using var dbContext = fixture.CreateControlPlaneDbContext();
        var tenant = Tenant.Create(TenantAlias.Create("globex"));
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var createdAt = tenant.CreatedAt;
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        tenant.RenameAlias(TenantAlias.Create("globex-two"));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(createdAt, tenant.CreatedAt);
        Assert.Equal(createdAt.AddMinutes(5), tenant.UpdatedAt);
    }

    [Fact]
    public async Task SavingThroughTheDiResolvedControlPlaneContextStampsCreatedAt()
    {
        // Arrange
        await using var fixture = await ControlPlaneFixture.StartAsync(isPlatformAdmin: false);
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var tenant = Tenant.Create(TenantAlias.Create("wiring"));

        // Act
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(default, tenant.CreatedAt);
    }

    [Fact]
    public async Task SavingThroughTheDiResolvedTenantDbContextFactoryStampsCreatedAt()
    {
        // Arrange
        await using var owner = await TenantSurfaceFixture.StartAsync();
        using var scope = owner.CreateScope();
        var tenantDbContextFactory = scope.ServiceProvider.GetRequiredService<TenantDbContextFactory>();
        await using var tenantDbContext = tenantDbContextFactory.Create(owner.Tenant.DatabaseName.Value);
        var role = await tenantDbContext.Roles.SingleAsync(
            candidate => candidate.Code == "member",
            TestContext.Current.CancellationToken);
        var user = User.Create(
            ExternalUserId.Create("user_wiring"),
            EmailAddress.Create("wiring@example.com"),
            UserStatus.Active,
            role);

        // Act
        tenantDbContext.Users.Add(user);
        await tenantDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(default, user.CreatedAt);
    }

    [Fact]
    public async Task ReplacingAMembersRoleThroughTheRealFlow_BumpsTheUsersUpdatedAt()
    {
        // Arrange
        const string invitedUserId = "user_invited";
        const string invitedEmail = "invited@example.com";
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        using var invitationResponse = await fixture.Client.PostAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/invitations",
            new { email = invitedEmail, roleCode = "member" },
            TestContext.Current.CancellationToken);
        var invitationBody = await invitationResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var token = invitationBody.GetProperty("token").GetString()!;
        await using var invited = fixture.WithPrincipal(invitedUserId, invitedEmail);
        using var acceptResponse = await invited.Client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        var externalUserId = ExternalUserId.Create(invitedUserId);
        await using var before = fixture.CreateTenantDbContext();
        var updatedAtBeforeRoleChange = await before.Users
            .AsNoTracking()
            .Where(user => user.ExternalUserId == externalUserId)
            .Select(user => user.UpdatedAt)
            .SingleAsync(TestContext.Current.CancellationToken);

        // Act
        using var response = await fixture.Client.PutAsJsonAsync(
            $"/{TenantSurfaceFixture.Alias}/api/v1/members/{invitedUserId}/role",
            new { roleCode = "owner" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var after = fixture.CreateTenantDbContext();
        var updatedAtAfterRoleChange = await after.Users
            .AsNoTracking()
            .Where(user => user.ExternalUserId == externalUserId)
            .Select(user => user.UpdatedAt)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(updatedAtAfterRoleChange > updatedAtBeforeRoleChange);
    }
}
