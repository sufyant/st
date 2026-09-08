using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class AdminSchemaForeignKeyTests(PostgresContainerFixture fixture)
{
    private AdminDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        return new AdminDbContext(options);
    }

    [Fact]
    public async Task AddMembership_WithTenantIdNotReferencingARealTenant_ThrowsDbUpdateException()
    {
        // Arrange: UserId references a real row, but TenantId is a dangling Guid.
        var user = User.Create($"clerk_{Guid.NewGuid():N}", Email.Create($"{Guid.NewGuid():N}@example.com"));
        var membership = Membership.Create(user.Id, Guid.NewGuid(), "member");

        await using var context = CreateContext();
        context.Users.Add(user);
        context.Memberships.Add(membership);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task AddInvitation_WithTenantIdNotReferencingARealTenant_ThrowsDbUpdateException()
    {
        // Arrange
        var invitation = Invitation.Create(Guid.NewGuid(), Email.Create($"{Guid.NewGuid():N}@example.com"), "member");

        await using var context = CreateContext();
        context.Invitations.Add(invitation);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
