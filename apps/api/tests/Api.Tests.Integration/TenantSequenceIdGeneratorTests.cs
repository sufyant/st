using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantSequenceIdGeneratorTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task NextAsync_CalledRepeatedly_ProducesSequentialFormattedIds()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var generator = new TenantSequenceIdGenerator(dbContext);
        var schemaName = "admin";
        var sequenceName = $"seq_test_{Guid.NewGuid():N}";

        // Act
        var first = await generator.NextAsync(schemaName, sequenceName, "INV");
        var second = await generator.NextAsync(schemaName, sequenceName, "INV");

        // Assert
        Assert.Equal("INV-000001", first);
        Assert.Equal("INV-000002", second);
    }

    [Theory]
    [InlineData("bad schema")]
    [InlineData("bad;schema")]
    [InlineData("BadSchema")]
    [InlineData("this_schema_name_is_way_too_long_and_exceeds_the_sixty_three_character_postgres_identifier_limit")]
    public async Task NextAsync_UnsafeSchemaName_ThrowsArgumentException(string schemaName)
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using var dbContext = new AdminDbContext(options);
        var generator = new TenantSequenceIdGenerator(dbContext);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => generator.NextAsync(schemaName, "seq_test", "INV"));
    }
}
