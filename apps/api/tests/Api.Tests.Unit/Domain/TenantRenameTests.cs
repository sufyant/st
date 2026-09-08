using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class TenantRenameTests
{
    [Fact]
    public void Rename_ValidInput_UpdatesNameAndRaisesDomainEvent()
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create("acme"), "Old Name");

        // Act
        tenant.Rename("New Name");

        // Assert
        Assert.Equal("New Name", tenant.Name);
        var domainEvent = Assert.Single(tenant.DomainEvents);
        var renamedEvent = Assert.IsType<TenantRenamedDomainEvent>(domainEvent);
        Assert.Equal(tenant.Id, renamedEvent.TenantId);
        Assert.Equal("New Name", renamedEvent.NewName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_EmptyName_ThrowsArgumentException(string newName)
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create("acme"), "Old Name");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => tenant.Rename(newName));
    }
}
