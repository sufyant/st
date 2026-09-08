using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
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
        // Arrange: Memberships.TenantId/UserId are now FK-constrained (Finding 5), so the
        // referenced Tenant/User rows must exist first.
        var tenant = Tenant.Create(TenantSlug.Create($"concur-{Guid.NewGuid():N}"[..15]), "Concurrency Test Tenant");
        var user = User.Create($"clerk_{Guid.NewGuid():N}", Email.Create($"{Guid.NewGuid():N}@example.com"));
        var membership = Membership.Create(user.Id, tenant.Id, "member");

        await using (var seedContext = CreateContext())
        {
            seedContext.Tenants.Add(tenant);
            seedContext.Users.Add(user);
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
