using Api.Application.Tenants;
using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Application;

public class RenameTenantCommandHandlerTests
{
    private sealed class FakeTenantRepository(Tenant? tenant) : ITenantRepository
    {
        public Task<Tenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(tenant is not null && tenant.Id == tenantId ? tenant : null);
    }

    [Fact]
    public async Task Handle_ExistingTenant_RenamesAndReturnsSuccess()
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create("acme"), "Old Name");
        var handler = new RenameTenantCommandHandler(new FakeTenantRepository(tenant));

        // Act
        var result = await handler.Handle(new RenameTenantCommand(tenant.Id, "New Name"), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", tenant.Name);
    }

    [Fact]
    public async Task Handle_UnknownTenant_ReturnsFailure()
    {
        // Arrange
        var handler = new RenameTenantCommandHandler(new FakeTenantRepository(null));

        // Act
        var result = await handler.Handle(new RenameTenantCommand(Guid.NewGuid(), "New Name"), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.NotFound", result.Error.Code);
    }
}
