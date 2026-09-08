# apps/api Phase 3: Auth + Tenant Resolution + Permission Middleware Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** JWT Bearer authentication, path-based tenant resolution with membership enforcement, and permission-based authorization — all proven end-to-end via a minimal diagnostic `/{tenant}/api/v1/whoami` endpoint, tested against real Postgres with locally-signed test JWTs (no network calls to a real Clerk instance).

**Architecture:** `Api.Infrastructure` gains `TenantResolutionMiddleware` (ASP.NET Core middleware) and `PermissionAuthorizationHandler`/`PermissionRequirement` (ASP.NET Core native authorization). `Api.Host`'s `Program.cs` wires JWT Bearer auth, the middleware, and authorization policies into the pipeline in this order: `UseRouting` → `UseAuthentication` → `TenantResolutionMiddleware` → `UseAuthorization` → endpoints. `Api.Tests.Integration` gains `CustomWebApplicationFactory` (overrides JWT validation to use a local test signing key instead of a real Authority/JWKS network call) and `TestJwtTokenFactory`.

**Tech Stack:** `Microsoft.AspNetCore.Authentication.JwtBearer`, `System.IdentityModel.Tokens.Jwt`, `Microsoft.AspNetCore.Mvc.Testing`.

## Global Constraints

- No real network calls to Clerk in any test — JWT validation is tested against a locally-generated RSA key via `CustomWebApplicationFactory`, never a live Authority/JWKS endpoint.
- No tenant-schema business entities or endpoints invented — the only new endpoint is the diagnostic `/{tenant}/api/v1/whoami` (same category as Phase 1's `/health`), not speculative product functionality.
- The permission-check mechanism in this phase is ASP.NET Core's native claims/policy-based authorization at the HTTP endpoint level — NOT a mediator pipeline behavior (the mediator doesn't exist until Phase 4). Don't build `[RequiresPermission]` or any mediator-shaped abstraction in this phase.
- Middleware pipeline order is binding: `UseRouting` → `UseAuthentication` → `TenantResolutionMiddleware` → `UseAuthorization` → endpoints. Getting this order wrong breaks the acceptance criteria (e.g. authorization running before tenant claims are attached).
- API-detail allowance applies (as in Phase 2b): if a specific API call doesn't compile or behave as described, adjust to the nearest correct equivalent and document the deviation.
- Before editing `apps/api/src/Api.Host/Program.cs` in any task, READ its current content first — it has been modified across Phase 1 and Phase 2b's tasks and fix waves; this plan's code snippets show what to ADD, not a full-file replacement to paste over the existing content.
- Work from `/Users/sufyan/Documents/Projects/st` (repo root). Docker must be running for integration tests.

---

### Task 1: JWT Bearer authentication + test JWT infrastructure

**Files:**
- Modify: `apps/api/src/Api.Host/Api.Host.csproj` (add `Microsoft.AspNetCore.Authentication.JwtBearer`)
- Modify: `apps/api/src/Api.Host/Program.cs` (add JWT Bearer auth, a minimal `[Authorize]`-only `/api/v1/whoami` endpoint)
- Modify: `apps/api/src/Api.Host/appsettings.json` (add `Clerk:Authority` placeholder)
- Modify: `apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj` (add project reference to `Api.Host`, packages `Microsoft.AspNetCore.Mvc.Testing` and `System.IdentityModel.Tokens.Jwt`)
- Create: `apps/api/tests/Api.Tests.Integration/CustomWebApplicationFactory.cs`
- Create: `apps/api/tests/Api.Tests.Integration/TestJwtTokenFactory.cs`
- Create: `apps/api/tests/Api.Tests.Integration/WhoAmIEndpointTests.cs`

**Interfaces:**
- Consumes: `PostgresContainerFixture`/`PostgresCollection` from Phase 2b Task 1; `AdminDbContext` from Phase 2b Task 3.
- Produces: `CustomWebApplicationFactory(string connectionString)` (a `WebApplicationFactory<Program>` that overrides JWT validation to a local key and re-points `AdminDbContext` at the given connection string), `TestJwtTokenFactory.CreateToken(string clerkUserId, TimeSpan? lifetime = null, SecurityKey? signingKey = null, string? issuer = null) : string`. Task 2's `WhoAmIEndpointTests` (extended, not replaced) and Task 3's `PermissionAuthorizationTests` both use this same factory/token infrastructure.

- [ ] **Step 1: Add the JWT Bearer package**

```bash
cd apps/api
dotnet add src/Api.Host/Api.Host.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
```

- [ ] **Step 2: Wire test project references and packages**

```bash
dotnet add tests/Api.Tests.Integration/Api.Tests.Integration.csproj reference src/Api.Host/Api.Host.csproj
dotnet add tests/Api.Tests.Integration/Api.Tests.Integration.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Api.Tests.Integration/Api.Tests.Integration.csproj package System.IdentityModel.Tokens.Jwt
```

- [ ] **Step 3: Add the Clerk authority placeholder to appsettings.json**

Read `apps/api/src/Api.Host/appsettings.json` first. Add a `Clerk` section alongside the existing `ConnectionStrings` section (merge, don't replace):

```json
{
  "Clerk": {
    "Authority": "https://CHANGE_ME.clerk.accounts.dev"
  }
}
```

- [ ] **Step 4: Wire JWT Bearer authentication and a minimal protected endpoint in Program.cs**

Read the current `apps/api/src/Api.Host/Program.cs` first. Add these using statements near the top:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
```

After the existing `AddDbContext<Api.Infrastructure.AdminDbContext>(...)` registration, add:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Clerk:Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Clerk:Authority"],
            ValidateAudience = false,
            ValidateLifetime = true,
            NameClaimType = "sub",
        };
    });

builder.Services.AddAuthorization();
```

After `var app = builder.Build();`, before the existing `app.MapGet("/health", ...)` line, add:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

After the existing `/health` endpoint mapping, add:

```csharp
app.MapGet("/api/v1/whoami", (HttpContext context) =>
{
    var userId = context.User.FindFirstValue("sub");
    return Results.Ok(new { userId });
}).RequireAuthorization();
```

Add `using System.Security.Claims;` if `FindFirstValue` doesn't resolve (it's an extension method on `ClaimsPrincipal`).

Leave the existing `public partial class Program;` line at the end exactly as it is.

- [ ] **Step 5: Implement the custom WebApplicationFactory**

Create `apps/api/tests/Api.Tests.Integration/CustomWebApplicationFactory.cs`:

```csharp
using System.Security.Cryptography;
using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Api.Tests.Integration;

public sealed class CustomWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    public static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048));

    public const string TestIssuer = "https://test-issuer.local";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = SigningKey,
                    NameClaimType = "sub",
                };
            });

            services.RemoveAll<DbContextOptions<AdminDbContext>>();
            services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(connectionString));
        });
    }
}
```

- [ ] **Step 6: Implement the test JWT token factory**

Create `apps/api/tests/Api.Tests.Integration/TestJwtTokenFactory.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Api.Tests.Integration;

public static class TestJwtTokenFactory
{
    public static string CreateToken(
        string clerkUserId,
        TimeSpan? lifetime = null,
        SecurityKey? signingKey = null,
        string? issuer = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(
            signingKey ?? CustomWebApplicationFactory.SigningKey, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: issuer ?? CustomWebApplicationFactory.TestIssuer,
            claims: [new Claim("sub", clerkUserId)],
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(5)),
            signingCredentials: credentials);

        return handler.WriteToken(token);
    }
}
```

- [ ] **Step 7: Write the endpoint tests**

Create `apps/api/tests/Api.Tests.Integration/WhoAmIEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class WhoAmIEndpointTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    public WhoAmIEndpointTests(PostgresContainerFixture fixture)
    {
        _factory = new CustomWebApplicationFactory(fixture.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetWhoAmI_NoToken_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_ValidToken_ReturnsSubClaim()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken("clerk_test_user_1");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WhoAmIResponse>();
        Assert.Equal("clerk_test_user_1", body?.UserId);
    }

    [Fact]
    public async Task GetWhoAmI_ExpiredToken_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken("clerk_test_user_1", lifetime: TimeSpan.FromMinutes(-5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_WrongSigningKey_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        using var wrongKey = RSA.Create(2048);
        var token = TestJwtTokenFactory.CreateToken(
            "clerk_test_user_1", signingKey: new RsaSecurityKey(wrongKey));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record WhoAmIResponse(string UserId);
}
```

- [ ] **Step 8: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~WhoAmIEndpointTests"`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 9: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall (86 pre-existing + 4 new = 90; recount from actual output rather than trusting this arithmetic).

- [ ] **Step 10: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add JWT Bearer authentication with local-key test infrastructure

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Path-based tenant resolution middleware

**Files:**
- Create: `apps/api/src/Api.Infrastructure/TenantResolutionMiddleware.cs`
- Modify: `apps/api/src/Api.Host/Program.cs` (insert the middleware into the pipeline, change the whoami route to include the tenant segment)
- Modify: `apps/api/tests/Api.Tests.Integration/WhoAmIEndpointTests.cs` (update existing tests' URLs to include a tenant segment, since the route is changing)
- Create: `apps/api/tests/Api.Tests.Integration/TenantResolutionMiddlewareTests.cs`

**Interfaces:**
- Consumes: `AdminDbContext`, `Tenant`, `TenantSlug`, `User`, `Membership` from Phase 2b; `CustomWebApplicationFactory`/`TestJwtTokenFactory` from Task 1.
- Produces: `TenantResolutionMiddleware` (adds `tenant_id`/`tenant_slug`/`membership_role` claims to `HttpContext.User` when tenant + membership resolve successfully; 404 if tenant alias doesn't resolve to a real `Tenant`; 403 if authenticated but no `Membership` exists for that tenant). Task 3's `PermissionAuthorizationHandler` reads the `membership_role` claim this middleware sets.

- [ ] **Step 1: Implement the middleware**

Create `apps/api/src/Api.Infrastructure/TenantResolutionMiddleware.cs`:

```csharp
using System.Security.Claims;
using Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AdminDbContext dbContext)
    {
        var alias = context.GetRouteValue("tenant") as string;

        if (string.IsNullOrEmpty(alias))
        {
            await next(context);
            return;
        }

        TenantSlug slug;
        try
        {
            slug = TenantSlug.Create(alias);
        }
        catch (ArgumentException)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug);

        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var clerkUserId = context.User.FindFirstValue("sub");
            var user = clerkUserId is null
                ? null
                : await dbContext.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.ClerkUserId == clerkUserId);

            var membership = user is null
                ? null
                : await dbContext.Memberships.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == tenant.Id);

            if (membership is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (context.User.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim("tenant_id", tenant.Id.ToString()));
                identity.AddClaim(new Claim("tenant_slug", tenant.Slug.Value));
                identity.AddClaim(new Claim("membership_role", membership.Role));
            }
        }

        await next(context);
    }
}
```

- [ ] **Step 2: Wire the middleware into the pipeline and change the route**

Read the current `apps/api/src/Api.Host/Program.cs`. Before `app.UseAuthentication();`, add:

```csharp
app.UseRouting();
```

Immediately after `app.UseAuthentication();`, add:

```csharp
app.UseMiddleware<Api.Infrastructure.TenantResolutionMiddleware>();
```

(So the order becomes: `UseRouting` → `UseAuthentication` → `UseMiddleware<TenantResolutionMiddleware>` → `UseAuthorization` → endpoint mappings.)

Change the whoami endpoint route from `/api/v1/whoami` to `/{tenant}/api/v1/whoami`, and include the tenant claims in the response:

```csharp
app.MapGet("/{tenant}/api/v1/whoami", (HttpContext context) =>
{
    var userId = context.User.FindFirstValue("sub");
    var tenantId = context.User.FindFirstValue("tenant_id");
    var role = context.User.FindFirstValue("membership_role");
    return Results.Ok(new { userId, tenantId, role });
}).RequireAuthorization();
```

Leave `/health` unchanged (no tenant prefix — it has no `{tenant}` route value, so `TenantResolutionMiddleware` passes it straight through).

- [ ] **Step 3: Update Task 1's existing whoami tests for the new route**

In `apps/api/tests/Api.Tests.Integration/WhoAmIEndpointTests.cs`, the existing 4 tests call `client.GetAsync("/api/v1/whoami")` — this route no longer exists (it's now `/{tenant}/api/v1/whoami`). Update each test to first provision a real tenant (via `TenantProvisioningService`, using a fresh `AdminDbContext` built from `CustomWebApplicationFactory`'s underlying connection — or simpler, construct a separate `AdminDbContext` directly against `fixture.ConnectionString` just for test setup, matching the pattern other Phase 2b integration tests use) and call `/{tenantSlug}/api/v1/whoami` instead. Since these 4 tests are about AUTHENTICATION only (not membership), the authenticated ones (`GetWhoAmI_ValidToken_ReturnsSubClaim`) will now hit the tenant-resolution middleware's membership check and get 403 (no `Membership` row exists for `"clerk_test_user_1"`) instead of 200 — that's fine, update this test's expectation to `HttpStatusCode.Forbidden`, since Task 1's job (proving JWT validation alone works) is still proven by the response NOT being 401. The `WhoAmIResponse` record and its assertion on `body?.UserId` should be removed from that test since a 403 has no such body. The no-token/expired-token/wrong-key tests still expect 401 (tenant resolution requires `IsAuthenticated == true` to even attempt a membership check, and an unauthenticated request never reaches that check before `UseAuthorization` rejects it with 401) — but note tenant EXISTENCE is checked before authentication status in the middleware, so these 3 tests must also provision a real tenant first, otherwise they'd get 404 instead of 401 and the test would be proving the wrong thing.

- [ ] **Step 4: Write the tenant-resolution-specific tests**

Create `apps/api/tests/Api.Tests.Integration/TenantResolutionMiddlewareTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Api.Domain;
using Api.Infrastructure;
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

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 5: Run the new and updated tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~WhoAmIEndpointTests|FullyQualifiedName~TenantResolutionMiddlewareTests"`
Expected: `Failed: 0` (4 updated + 3 new = 7).

- [ ] **Step 6: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

- [ ] **Step 7: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add path-based tenant resolution middleware with membership enforcement

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Permission-based authorization

**Files:**
- Create: `apps/api/src/Api.Infrastructure/PermissionRequirement.cs`
- Create: `apps/api/src/Api.Infrastructure/PermissionAuthorizationHandler.cs`
- Modify: `apps/api/src/Api.Host/Program.cs` (register the permission policy + handler, apply the policy to the whoami endpoint)
- Create: `apps/api/tests/Api.Tests.Integration/PermissionAuthorizationTests.cs`

**Interfaces:**
- Consumes: `AdminDbContext`, `RolePermission` from Phase 2b; `membership_role` claim set by Task 2's `TenantResolutionMiddleware`; `CustomWebApplicationFactory`/`TestJwtTokenFactory` from Task 1.
- Produces: `PermissionRequirement(string permission)`, `PermissionAuthorizationHandler` (an `AuthorizationHandler<PermissionRequirement>` that checks the `membership_role` claim against `RolePermissions`), the `"tenant.whoami"` authorization policy.

- [ ] **Step 1: Implement the permission requirement and handler**

Create `apps/api/src/Api.Infrastructure/PermissionRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace Api.Infrastructure;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
```

Create `apps/api/src/Api.Infrastructure/PermissionAuthorizationHandler.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class PermissionAuthorizationHandler(AdminDbContext dbContext)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var role = context.User.FindFirst("membership_role")?.Value;

        if (string.IsNullOrEmpty(role))
        {
            return;
        }

        var hasPermission = await dbContext.RolePermissions
            .AsNoTracking()
            .AnyAsync(rp => rp.Role == role && rp.Permission == requirement.Permission);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
```

- [ ] **Step 2: Register the policy and handler, apply the policy to the endpoint**

Read the current `apps/api/src/Api.Host/Program.cs`. Change `builder.Services.AddAuthorization();` to:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("tenant.whoami", policy =>
        policy.Requirements.Add(new Api.Infrastructure.PermissionRequirement("tenant.whoami")));
});
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Api.Infrastructure.PermissionAuthorizationHandler>();
```

Change the whoami endpoint's `.RequireAuthorization()` to `.RequireAuthorization("tenant.whoami")`.

- [ ] **Step 3: Write the permission tests**

Create `apps/api/tests/Api.Tests.Integration/PermissionAuthorizationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class PermissionAuthorizationTests : IDisposable
{
    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;

    public PermissionAuthorizationTests(PostgresContainerFixture fixture)
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

    private async Task<(Tenant Tenant, string ClerkUserId)> SeedTenantAndMembershipAsync(string role)
    {
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"perm-{Guid.NewGuid():N}"[..15]), "Permission Test Tenant");

        var clerkUserId = $"clerk_perm_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        var membership = Membership.Create(user.Id, tenant.Id, role);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        return (tenant, clerkUserId);
    }

    [Fact]
    public async Task GetWhoAmI_RoleWithoutPermission_ReturnsForbidden()
    {
        // Arrange
        var (tenant, clerkUserId) = await SeedTenantAndMembershipAsync("guest");
        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenant.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_RoleWithPermission_ReturnsOk()
    {
        // Arrange
        var (tenant, clerkUserId) = await SeedTenantAndMembershipAsync("member");

        await using (var dbContext = CreateDbContext())
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
}
```

**Note:** because `RolePermission.(Role, Permission)` has a unique index (Phase 2b), and different test methods within this class use different randomly-generated tenant/user data but the SAME literal role strings (`"guest"`, `"member"`) and the SAME literal permission string (`"tenant.whoami"`), running `GetWhoAmI_RoleWithPermission_ReturnsOk` more than once across test runs (e.g. via `dotnet test` retries, or if xUnit ever re-executes it) would attempt to insert a duplicate `("member", "tenant.whoami")` row and fail with a unique constraint violation. If you hit this, wrap the `RolePermissions.Add(...)` in a check-then-add (`AnyAsync` first) or a try/catch around `SaveChangesAsync` treating a duplicate-key violation as success, rather than assuming a single insert is always safe — investigate the actual test run behavior rather than guessing.

- [ ] **Step 4: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~PermissionAuthorizationTests"`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 5: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

- [ ] **Step 6: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add permission-based authorization via RolePermission

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: End-to-end pipeline verification

**Files:**
- Create: `apps/api/tests/Api.Tests.Integration/AuthPipelineEndToEndTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-3. No new production code — this task only adds one consolidated test file walking through all 5 of the phase spec's HTTP-behavior acceptance criteria in sequence against a single, realistic scenario, plus verifies acceptance criterion 6 (`pnpm` build/test).

- [ ] **Step 1: Write the end-to-end test**

Create `apps/api/tests/Api.Tests.Integration/AuthPipelineEndToEndTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~AuthPipelineEndToEndTests"`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 3: Run the full solution and the pnpm gates**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

Run (from repo root): `pnpm --filter api build` and `pnpm --filter api test`
Expected: both succeed (phase spec acceptance criterion 6).

- [ ] **Step 4: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "test(api): add end-to-end auth+tenant+permission pipeline test

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
