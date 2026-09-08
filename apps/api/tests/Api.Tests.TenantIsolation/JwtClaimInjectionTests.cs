using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.TenantIsolation;

[Collection(nameof(PostgresCollection))]
public sealed class JwtClaimInjectionTests(PostgresContainerFixture fixture) : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new(fixture.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private AdminDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        return new AdminDbContext(options);
    }

    // RolePermission.(Role, Permission) has a unique index, and other test projects/classes
    // against this same shared test database may already seed ("owner", "tenant.whoami").
    // Check-then-add avoids a duplicate-key violation.
    private async Task EnsureRolePermissionAsync(string role, string permission)
    {
        await using var dbContext = CreateDbContext();
        var exists = await dbContext.RolePermissions
            .AnyAsync(rp => rp.Role == role && rp.Permission == permission);
        if (exists)
        {
            return;
        }

        dbContext.RolePermissions.Add(RolePermission.Create(role, permission));
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ForgedTenantIdClaimInJwt_CannotOverrideDbDerivedTenantContext()
    {
        // Arrange: the caller has real membership in tenantA. Their JWT carries a forged
        // "tenant_id" claim pointing at a different, real tenantB. If TenantResolutionMiddleware
        // failed to strip pre-existing claims of these types before adding its own DB-derived
        // ones, ClaimsPrincipal.FindFirst("tenant_id") could return the forged value instead.
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenantA = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-jwt-a-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var tenantB = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-jwt-b-{Guid.NewGuid():N}"[..15]), "Tenant B");

        var clerkUserId = $"clerk_iso_jwt_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenantA.Id, "owner"));
        await dbContext.SaveChangesAsync();

        await EnsureRolePermissionAsync("owner", "tenant.whoami");

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(
            clerkUserId, extraClaims: [new Claim("tenant_id", tenantB.Id.ToString())]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenantA.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tenantA.Id.ToString(), body.GetProperty("tenantId").GetString());
    }
}
