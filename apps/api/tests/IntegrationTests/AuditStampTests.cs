using Domain.ControlPlane.Tenants;
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
}
