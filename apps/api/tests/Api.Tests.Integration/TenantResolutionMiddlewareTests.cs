using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantResolutionMiddlewareTests : IDisposable
{
    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;

    public TenantResolutionMiddlewareTests(PostgresContainerFixture fixture)
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
    public async Task GetWhoAmI_UnknownTenant_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken("clerk_unknown_tenant_test");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/does-not-exist-{Guid.NewGuid():N}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_AuthenticatedNoMembership_ReturnsForbidden()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"noaccess-{Guid.NewGuid():N}"[..20]), "No Access Tenant");

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken($"clerk_no_membership_{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_AuthenticatedWithMembership_ReturnsOkWithTenantClaims()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"hasaccess-{Guid.NewGuid():N}"[..20]), "Has Access Tenant");

        var clerkUserId = $"clerk_has_membership_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        var membership = Membership.Create(user.Id, tenant.Id, "member");
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        // "member" must also be granted the "tenant.whoami" permission for this
        // request to succeed now that the endpoint enforces PermissionAuthorizationHandler.
        // Check-then-add: RolePermission.(Role, Permission) has a unique index and
        // PermissionAuthorizationTests seeds this same pair.
        var hasPermission = await dbContext.RolePermissions
            .AnyAsync(rp => rp.Role == "member" && rp.Permission == "tenant.whoami");
        if (!hasPermission)
        {
            dbContext.RolePermissions.Add(RolePermission.Create("member", "tenant.whoami"));
            await dbContext.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_TokenClaimsCannotOverrideDatabaseDerivedRole_UsesRealMembershipRole()
    {
        // Arrange: real membership role is "guest" (no tenant.whoami permission), but the
        // JWT itself also carries a "membership_role" claim of "owner" - and "owner" DOES
        // have the tenant.whoami permission granted elsewhere in this test's seed data. If
        // the JWT-supplied claim were allowed to shadow the DB-derived one, this request
        // would incorrectly succeed with 200 instead of 403.
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"shadow-{Guid.NewGuid():N}"[..20]), "Claim Shadow Tenant");

        var clerkUserId = $"clerk_claim_shadow_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        var membership = Membership.Create(user.Id, tenant.Id, "guest");
        dbContext.Memberships.Add(membership);
        dbContext.RolePermissions.Add(RolePermission.Create("owner", "tenant.whoami"));
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(
            clerkUserId,
            extraClaims: [new Claim("membership_role", "owner")]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_InjectedPermissionClaimCannotGrantAccess_UsesRealMembershipPermissions()
    {
        // Arrange: real membership role is "guest", which has no RolePermission row granting
        // "tenant.whoami". The JWT itself also carries a raw "permission" claim of
        // "tenant.whoami" - the exact claim type PermissionAuthorizationHandler checks via
        // context.User.HasClaim("permission", "tenant.whoami"). If the stale-claim-removal
        // loop in TenantResolutionMiddleware did not strip this claim type (or ever dropped
        // "permission" from its claim-type array), this injected claim would directly satisfy
        // the authorization handler and incorrectly grant access. This is the most direct,
        // currently-live proof that the removal loop closes the claim-injection vulnerability.
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"permshadow-{Guid.NewGuid():N}"[..20]), "Permission Shadow Tenant");

        var clerkUserId = $"clerk_perm_shadow_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        var membership = Membership.Create(user.Id, tenant.Id, "guest");
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(
            clerkUserId,
            extraClaims: [new Claim("permission", "tenant.whoami")]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_InjectedTenantIdClaimDoesNotLeakIntoResponse_UsesRealResolvedTenant()
    {
        // Arrange: the JWT carries an extra "tenant_id" claim pointing at a DIFFERENT tenant
        // (one the caller has no membership in) than the alias actually resolved in the
        // route. If the stale-claim-removal loop didn't strip this claim type before the
        // middleware adds its own DB-derived "tenant_id", ClaimsPrincipal.FindFirst could
        // return the injected value first, leaking a foreign tenant id into the response.
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var realTenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"realtid-{Guid.NewGuid():N}"[..20]), "Real Tenant");
        var otherTenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"othertid-{Guid.NewGuid():N}"[..20]), "Other Tenant");

        var clerkUserId = $"clerk_tid_shadow_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        var membership = Membership.Create(user.Id, realTenant.Id, "member");
        dbContext.Memberships.Add(membership);

        var hasPermission = await dbContext.RolePermissions
            .AnyAsync(rp => rp.Role == "member" && rp.Permission == "tenant.whoami");
        if (!hasPermission)
        {
            dbContext.RolePermissions.Add(RolePermission.Create("member", "tenant.whoami"));
        }

        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(
            clerkUserId,
            extraClaims: [new Claim("tenant_id", otherTenant.Id.ToString())]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{realTenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tenantIdInResponse = body.RootElement.GetProperty("tenantId").GetString();
        Assert.Equal(realTenant.Id.ToString(), tenantIdInResponse);
        Assert.NotEqual(otherTenant.Id.ToString(), tenantIdInResponse);
    }

    [Fact]
    public async Task GetWhoAmI_RepeatedRequestsToSameTenant_BothSucceedFromCachedResolution()
    {
        // Arrange: the tenant-resolution lookup (alias -> tenant) is cached via IMemoryCache
        // (ADR 0017). Two consecutive requests to the same tenant alias must both resolve
        // correctly - the second one exercising the cached path.
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"cached-{Guid.NewGuid():N}"[..20]), "Cached Tenant");

        var clerkUserId = $"clerk_cached_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        var membership = Membership.Create(user.Id, tenant.Id, "member");
        dbContext.Memberships.Add(membership);

        var hasPermission = await dbContext.RolePermissions
            .AnyAsync(rp => rp.Role == "member" && rp.Permission == "tenant.whoami");
        if (!hasPermission)
        {
            dbContext.RolePermissions.Add(RolePermission.Create("member", "tenant.whoami"));
        }

        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var firstResponse = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");
        var secondResponse = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    }
}
