using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class TenantTests
{
    [Fact]
    public void Create_ValidInput_SetsPropertiesAndDerivesSchemaName()
    {
        // Arrange
        var slug = TenantSlug.Create("acme-corp");

        // Act
        var tenant = Tenant.Create(slug, "Acme Corp");

        // Assert
        Assert.Equal(slug, tenant.Slug);
        Assert.Equal("Acme Corp", tenant.Name);
        Assert.Equal("tenant_acme_corp", tenant.SchemaName);
        Assert.NotEqual(Guid.Empty, tenant.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_ThrowsArgumentException(string name)
    {
        // Arrange
        var slug = TenantSlug.Create("acme");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => Tenant.Create(slug, name));
    }
}
