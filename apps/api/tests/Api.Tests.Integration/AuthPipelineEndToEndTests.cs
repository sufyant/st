using System.Net;
using System.Net.Http.Headers;
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class AuthPipelineEndToEndTests : IDisposable
{
    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;

    public AuthPipelineEndToEndTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
        _factory = new CustomWebApplicationFactory(fixture.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private AdminDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new AdminDbContext(options);
    }

    [Fact]
    public async Task FullPipeline_WalksThroughAllFiveAcceptanceCriteria()
    {
        // Arrange: provision a real tenant, a user with a "lead" role, and a
        // RolePermission granting "tenant.whoami" to that role.
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "E2E Test Tenant");

        var clerkUserId = $"clerk_e2e_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        var membership = Membership.Create(user.Id, tenant.Id, "lead");
        dbContext.Memberships.Add(membership);
        dbContext.RolePermissions.Add(RolePermission.Create("lead", "tenant.whoami"));
        await dbContext.SaveChangesAsync();

        var whoamiUrl = $"/{tenant.Slug.Value}/api/v1/whoami";

        // Act & Assert 1: no token -> 401.
        var anonymousClient = _factory.CreateClient();
        var noTokenResponse = await anonymousClient.GetAsync(whoamiUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, noTokenResponse.StatusCode);

        // Act & Assert 2: valid token, no membership at a DIFFERENT real tenant -> 403.
        var otherTenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"e2e-other-{Guid.NewGuid():N}"[..15]), "E2E Other Tenant");
        var clientWithToken = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        clientWithToken.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var noMembershipResponse = await clientWithToken.GetAsync($"/{otherTenant.Slug.Value}/api/v1/whoami");
        Assert.Equal(HttpStatusCode.Forbidden, noMembershipResponse.StatusCode);

        // Act & Assert 3: unknown tenant alias -> 404.
        var unknownTenantResponse = await clientWithToken.GetAsync(
            $"/unknown-{Guid.NewGuid():N}"[..30] + "/api/v1/whoami");
        Assert.Equal(HttpStatusCode.NotFound, unknownTenantResponse.StatusCode);

        // Act & Assert 4: valid token + membership, role WITHOUT the required
        // permission -> 403. Give this same user a second membership, in a
        // third tenant, with a role that has no RolePermission grant.
        var noPermTenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"e2e-noperm-{Guid.NewGuid():N}"[..15]), "E2E No-Permission Tenant");
        dbContext.Memberships.Add(Membership.Create(user.Id, noPermTenant.Id, "unprivileged"));
        await dbContext.SaveChangesAsync();
        var noPermResponse = await clientWithToken.GetAsync($"/{noPermTenant.Slug.Value}/api/v1/whoami");
        Assert.Equal(HttpStatusCode.Forbidden, noPermResponse.StatusCode);

        // Act & Assert 5: valid token + membership + required permission -> 200.
        var successResponse = await clientWithToken.GetAsync(whoamiUrl);
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
    }
}
