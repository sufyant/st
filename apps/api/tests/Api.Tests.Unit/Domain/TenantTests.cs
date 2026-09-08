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

    [Fact]
    public void Create_WithMaxLengthSlug_SchemaNameIsExactlySixtyThreeCharacters()
    {
        // Arrange: 56-char slug is TenantSlug's max (63-byte Postgres identifier limit
        // minus the 7-char "tenant_" prefix), so the resulting schema name should be
        // exactly 63 characters - the real Postgres limit, not truncated.
        var slug = TenantSlug.Create(new string('a', 56));

        // Act
        var tenant = Tenant.Create(slug, "Max Slug Tenant");

        // Assert
        Assert.Equal(63, tenant.SchemaName.Length);
        Assert.Equal("tenant_" + new string('a', 56), tenant.SchemaName);
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
