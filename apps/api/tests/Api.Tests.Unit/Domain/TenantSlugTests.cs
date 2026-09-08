using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class TenantSlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp")]
    [InlineData("acme-corp-2")]
    public void Create_ValidInput_Succeeds(string input)
    {
        // Act
        var slug = TenantSlug.Create(input);

        // Assert
        Assert.Equal(input, slug.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Acme")]
    [InlineData("acme_corp")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme--corp")]
    [InlineData("admin")]
    [InlineData("public")]
    public void Create_InvalidInput_ThrowsArgumentException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => TenantSlug.Create(input));
    }

    [Fact]
    public void Create_AtPostgresSchemaNameLimit_Succeeds()
    {
        // Arrange: 56 chars + "tenant_" (7 chars) prefix == 63-byte Postgres identifier limit.
        var atLimit = new string('a', 56);

        // Act
        var slug = TenantSlug.Create(atLimit);

        // Assert
        Assert.Equal(atLimit, slug.Value);
    }

    [Fact]
    public void Create_ExceedsPostgresSchemaNameLimit_ThrowsArgumentException()
    {
        // Arrange: 57 chars would produce a 64-byte "tenant_"-prefixed schema name.
        var tooLong = new string('a', 57);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => TenantSlug.Create(tooLong));
    }
}
