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
    public void Create_ExceedsPostgresSchemaNameLimit_ThrowsArgumentException()
    {
        // Arrange
        var tooLong = new string('a', 64);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => TenantSlug.Create(tooLong));
    }
}
