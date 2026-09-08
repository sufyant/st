using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class AuditableInterceptorTests(PostgresContainerFixture fixture)
{
    private AdminDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(new AuditableSaveChangesInterceptor())
            .Options;

        return new AdminDbContext(options);
    }

    [Fact]
    public async Task AddUser_SetsCreatedAndUpdatedAtUtcOnInsert()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;
        var user = User.Create($"clerk_{Guid.NewGuid():N}", Email.Create($"{Guid.NewGuid():N}@example.com"));

        // Act
        await using var context = CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.InRange(user.CreatedAtUtc, before, after);
        Assert.InRange(user.UpdatedAtUtc, before, after);
    }

    [Fact]
    public async Task UpdateUser_OnlyUpdatesAtUtcChanges_CreatedAtUtcStaysFixed()
    {
        // Arrange
        var user = User.Create($"clerk_{Guid.NewGuid():N}", Email.Create($"{Guid.NewGuid():N}@example.com"));

        await using var seedContext = CreateContext();
        seedContext.Users.Add(user);
        await seedContext.SaveChangesAsync();
        var originalCreatedAt = user.CreatedAtUtc;

        await Task.Delay(TimeSpan.FromMilliseconds(50));

        // Act
        user.SetTimeZone("Europe/Istanbul");
        await seedContext.SaveChangesAsync();

        // Assert
        Assert.Equal(originalCreatedAt, user.CreatedAtUtc);
        Assert.True(user.UpdatedAtUtc > originalCreatedAt);
    }
}
