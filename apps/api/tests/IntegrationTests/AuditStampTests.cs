using Domain.Authorization;
using Domain.ControlPlane.Tenants;
using Domain.Shared;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
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
        var user = User.Create(
            ExternalUserId.Create("user_wiring"),
            EmailAddress.Create("wiring@example.com"),
            UserStatus.Active);

        // Act
        tenantDbContext.Users.Add(user);
        await tenantDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(default, user.CreatedAt);
    }
}
