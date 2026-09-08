using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class MembershipConcurrencyTests(PostgresContainerFixture fixture)
{
    private AdminDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        return new AdminDbContext(options);
    }

    [Fact]
    public async Task ConcurrentRoleChange_SecondSaveThrowsDbUpdateConcurrencyException()
    {
        // Arrange
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), "member");

        await using (var seedContext = CreateContext())
        {
            seedContext.Memberships.Add(membership);
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstView = await firstContext.Memberships.SingleAsync(m => m.Id == membership.Id);
        var secondView = await secondContext.Memberships.SingleAsync(m => m.Id == membership.Id);

        // Act
        firstView.ChangeRole("admin");
        await firstContext.SaveChangesAsync();

        secondView.ChangeRole("owner");

        // Assert
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }
}
