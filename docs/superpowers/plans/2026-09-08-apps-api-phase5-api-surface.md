# apps/api Phase 5: API Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A real REST endpoint (`PUT /{tenant-alias}/api/v1/tenant`) dispatching Phase 4's `RenameTenantCommand` through the full auth+tenant+permission+mediator pipeline over HTTP, an OpenAPI document, a generated TypeScript client in `packages/api-client`, and a minimal SignalR hub.

**Architecture:** The endpoint reads the resolved `tenant_id` claim (Phase 3's `TenantResolutionMiddleware` already verified it) rather than accepting a tenant id from the request body — this makes the endpoint's own request shape immune to the cross-tenant mismatch class of bug Phase 4's final review found, while `PermissionBehavior`'s `ITenantScopedRequest` check remains as defense-in-depth for any other future caller of the same command. `Result`/`Result<T>` map to HTTP status codes via `ResultHttpMapper`. `Microsoft.AspNetCore.OpenApi` generates the OpenAPI document; `Microsoft.Extensions.ApiDescription.Server` exports it to a JSON file at build time (no running server needed); `openapi-typescript` + `openapi-fetch` (pure npm, no external codegen tool) turn that into `packages/api-client`'s generated types + a thin typed client.

**Tech Stack:** `Microsoft.AspNetCore.OpenApi`, `Microsoft.Extensions.ApiDescription.Server`, `Microsoft.AspNetCore.SignalR` (server, already in ASP.NET Core shared framework) + `Microsoft.AspNetCore.SignalR.Client` (test-only); `openapi-typescript`, `openapi-fetch` (packages/api-client).

## Global Constraints

- Permission enforcement for THIS endpoint happens inside the mediator pipeline (`PermissionBehavior`, established in Phase 4) — the endpoint itself only needs `.RequireAuthorization()` (bare authentication), NOT a specific ASP.NET Core policy. This is the reconciliation Phase 4's final review deferred: HTTP-layer policies (`PermissionAuthorizationHandler`, Phase 3) remain for endpoints that don't dispatch through the mediator (like `/whoami`); mediator-dispatched endpoints rely on the pipeline's own `PermissionBehavior` instead. Do not add a redundant `.RequireAuthorization("tenant.rename")` policy for the new endpoint.
- The new endpoint's route is `/{tenant-alias}/api/v1/tenant` (singular, no separate tenant id in the path) — the tenant being modified is always "the one resolved by the URL prefix", read from the `tenant_id` claim, never a client-supplied id. This is a deliberate, narrower design than a generic `/tenants/{id}` REST shape, chosen specifically to make this endpoint structurally immune to the Phase 4 cross-tenant bug class.
- `packages/api-client`'s generated files ARE committed to the repo (not gitignored) — this keeps `apps/web`'s future builds independent of `apps/api` being buildable/running, consistent with how a generated client is normally treated in this kind of monorepo. The generation script re-runs and overwrites them; a stale commit is a `git diff` a reviewer would catch, not a silent problem.
- No new business functionality beyond exposing the already-built `RenameTenantCommand` and a diagnostic SignalR hub — same "no speculative product features" discipline as every earlier phase.
- API-detail allowance applies strongly to Task 4 (SignalR + `WebApplicationFactory` testing has known configuration quirks) and to Task 3's exact codegen tool invocation.
- Work from `/Users/sufyan/Documents/Projects/st` (repo root). Docker must be running for integration tests.

---

### Task 1: `Result` → HTTP mapping and the real `RenameTenant` endpoint

**Files:**
- Create: `apps/api/src/Api.Host/ResultHttpMapper.cs`
- Create: `apps/api/src/Api.Host/RenameTenantRequestBody.cs`
- Modify: `apps/api/src/Api.Host/Program.cs` (add the endpoint)
- Create: `apps/api/tests/Api.Tests.Integration/RenameTenantEndpointTests.cs`

**Interfaces:**
- Consumes: `IMediator`, `RenameTenantCommand`, `Result`/`Result<T>` from Phase 4; `CustomWebApplicationFactory`/`TestJwtTokenFactory` from Phase 3.
- Produces: `PUT /{tenant-alias}/api/v1/tenant` — reads `tenant_id` from the resolved claims, dispatches `RenameTenantCommand`, maps the `Result` to an HTTP response. Task 2's OpenAPI document will describe this endpoint automatically (no manual OpenAPI annotation needed beyond what minimal APIs infer, unless Task 2 finds gaps).

- [ ] **Step 1: Implement `ResultHttpMapper`**

Create `apps/api/src/Api.Host/ResultHttpMapper.cs`:

```csharp
using Api.Application;

namespace Api.Host;

public static class ResultHttpMapper
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : MapError(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : MapError(result.Error);

    private static IResult MapError(Error error)
    {
        var statusCode = error.Code switch
        {
            _ when error.Code.StartsWith("Validation.", StringComparison.Ordinal) =>
                StatusCodes.Status400BadRequest,
            "Permission.Denied" or "Tenant.Mismatch" => StatusCodes.Status403Forbidden,
            _ when error.Code.EndsWith(".NotFound", StringComparison.Ordinal) =>
                StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(detail: error.Message, statusCode: statusCode, title: error.Code);
    }
}
```

- [ ] **Step 2: Implement the request body DTO**

Create `apps/api/src/Api.Host/RenameTenantRequestBody.cs`:

```csharp
namespace Api.Host;

public sealed record RenameTenantRequestBody(string NewName);
```

- [ ] **Step 3: Wire the endpoint**

Read the current `apps/api/src/Api.Host/Program.cs` first. Add `using System.Security.Claims;` if not already present (it likely already is, from the `/whoami` endpoint). After the existing `/{tenant-alias}/api/v1/whoami` endpoint mapping, add:

```csharp
app.MapPut("/{tenant-alias}/api/v1/tenant", async (
    HttpContext context,
    Api.Host.RenameTenantRequestBody body,
    Api.Application.IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var tenantIdClaim = context.User.FindFirstValue("tenant_id");

    if (tenantIdClaim is null || !Guid.TryParse(tenantIdClaim, out var tenantId))
    {
        return Results.Forbid();
    }

    var result = await mediator.Send(
        new Api.Application.Tenants.RenameTenantCommand(tenantId, body.NewName), cancellationToken);
    return Api.Host.ResultHttpMapper.ToHttpResult(result);
})
.RequireAuthorization();
```

- [ ] **Step 4: Write the endpoint integration tests**

Create `apps/api/tests/Api.Tests.Integration/RenameTenantEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Domain;
using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class RenameTenantEndpointTests : IDisposable
{
    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;

    public RenameTenantEndpointTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
        _factory = new CustomWebApplicationFactory(fixture.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private AdminDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(_fixture.ConnectionString).Options;
        return new AdminDbContext(options);
    }

    [Fact]
    public async Task PutRenameTenant_ValidRequestWithPermission_RenamesAndReturns204()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"api-{Guid.NewGuid():N}"[..15]), "Original Name");

        var clerkUserId = $"clerk_api_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenant.Id, "owner"));
        dbContext.RolePermissions.Add(RolePermission.Create("owner", "tenant.rename"));
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PutAsJsonAsync(
            $"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "Renamed via HTTP" });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Renamed via HTTP", reloaded.Name);
    }

    [Fact]
    public async Task PutRenameTenant_NoToken_Returns401()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"api-{Guid.NewGuid():N}"[..15]), "Original Name");
        var client = _factory.CreateClient();

        // Act
        var response = await client.PutAsJsonAsync(
            $"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "Should Not Apply" });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutRenameTenant_NoPermission_Returns403AndDoesNotPersist()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"api-{Guid.NewGuid():N}"[..15]), "Original Name");

        var clerkUserId = $"clerk_api_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenant.Id, "guest"));
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PutAsJsonAsync(
            $"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "Should Not Apply" });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var verifyContext = CreateDbContext();
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Original Name", reloaded.Name);
    }

    [Fact]
    public async Task PutRenameTenant_EmptyName_Returns400()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"api-{Guid.NewGuid():N}"[..15]), "Original Name");

        var clerkUserId = $"clerk_api_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenant.Id, "owner"));
        dbContext.RolePermissions.Add(RolePermission.Create("owner", "tenant.rename"));
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PutAsJsonAsync(
            $"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

Note: `RolePermission.Create("owner", "tenant.rename")` — if a prior test already inserted a `("owner", "tenant.rename")` row and the unique index rejects the duplicate on a shared test database, use a check-then-add pattern (`AnyAsync` first) the same way `PermissionAuthorizationTests.cs` from Phase 3 already does, rather than assuming a bare `Add` is always safe — check that file's existing pattern first and reuse it if you hit a collision.

- [ ] **Step 5: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~RenameTenantEndpointTests"`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 6: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall (124 pre-existing + 4 new = 128; recount from actual output).

- [ ] **Step 7: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): expose RenameTenantCommand as a real REST endpoint

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: OpenAPI document generation

**Files:**
- Modify: `apps/api/src/Api.Host/Api.Host.csproj` (add `Microsoft.AspNetCore.OpenApi`, `Microsoft.Extensions.ApiDescription.Server`; set `OpenApiGenerateDocumentsOnBuild`)
- Modify: `apps/api/src/Api.Host/Program.cs` (add `AddOpenApi()`/`MapOpenApi()`)
- Create: `apps/api/tests/Api.Tests.Integration/OpenApiDocumentTests.cs`

**Interfaces:**
- Consumes: the endpoint from Task 1 (should appear in the generated document automatically via ASP.NET Core's minimal-API OpenAPI metadata inference).
- Produces: `GET /openapi/v1.json` at runtime (used by Task 2's own test); a build-time JSON file (used by Task 3's codegen — confirm and record its exact output path once you see it, since `Microsoft.Extensions.ApiDescription.Server`'s default path/filename may not match this plan's assumptions).

- [ ] **Step 1: Add the OpenAPI packages**

```bash
cd apps/api
dotnet add src/Api.Host/Api.Host.csproj package Microsoft.AspNetCore.OpenApi
dotnet add src/Api.Host/Api.Host.csproj package Microsoft.Extensions.ApiDescription.Server
```

- [ ] **Step 2: Enable build-time document generation**

Read `apps/api/src/Api.Host/Api.Host.csproj`. Add to its `<PropertyGroup>` (the main one, alongside `TargetFramework` etc.):

```xml
<OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>
<OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/../../openapi</OpenApiDocumentsDirectory>
```

(This targets `apps/api/openapi/` as the output directory — a stable, predictable location outside `obj/`/`bin/` that Task 3's TypeScript codegen can reliably point at, and that survives a `dotnet clean`. If `Microsoft.Extensions.ApiDescription.Server`'s actual behavior differs from this — e.g. a different MSBuild property name for this version, or it insists on writing under `obj/` regardless — adjust to whatever actually works and clearly record the REAL output path and filename in your report; Task 3 depends on knowing this exactly.)

- [ ] **Step 3: Register OpenAPI services and the document endpoint**

Read the current `apps/api/src/Api.Host/Program.cs`. Add `builder.Services.AddOpenApi();` alongside the other `builder.Services.Add...` calls. Add `app.MapOpenApi();` alongside the other `app.Map...`/`app.Use...` calls (order relative to auth/routing doesn't matter much for a docs endpoint, but keep it out of the way of the `TenantResolutionMiddleware`/authorization pipeline — e.g. register it right after `app.UseRouting()` and before `app.UseAuthentication()`, so the docs endpoint itself doesn't require auth).

- [ ] **Step 4: Build and locate the generated document**

Run: `dotnet build apps/api/Api.sln`
Then find the generated file: `find apps/api -name "*.json" -newer apps/api/Api.sln -path "*openapi*" 2>/dev/null` (or check whatever directory Step 2 actually configured). Confirm it's valid JSON containing an `openapi` version field and a `paths` object with `/{tenant-alias}/api/v1/tenant` and `/{tenant-alias}/api/v1/whoami` listed.

- [ ] **Step 5: Write the runtime-endpoint test**

Create `apps/api/tests/Api.Tests.Integration/OpenApiDocumentTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class OpenApiDocumentTests(PostgresContainerFixture fixture) : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new(fixture.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetOpenApiDocument_ReturnsValidJsonDescribingKnownEndpoints()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/openapi/v1.json");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/{tenant-alias}/api/v1/tenant", out _));
        Assert.True(paths.TryGetProperty("/{tenant-alias}/api/v1/whoami", out _));
    }
}
```

If the actual document route isn't `/openapi/v1.json` (check what `MapOpenApi()` actually registers in the installed package version — it may differ), adjust the test's URL and document the real route in your report.

- [ ] **Step 6: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~OpenApiDocumentTests"`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 7: Run the full solution once**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

- [ ] **Step 8: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): generate OpenAPI document at runtime and build time

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: `packages/api-client` — TypeScript client generation

**Files:**
- Create: `packages/api-client/package.json`
- Create: `packages/api-client/tsconfig.json`
- Create: `packages/api-client/src/client.ts`
- Create: `packages/api-client/src/schema.d.ts` (generated — committed, see Global Constraints)
- Modify: `packages/api-client/.gitkeep` → delete (package is no longer empty)

**Interfaces:**
- Consumes: the OpenAPI JSON file Task 2 produces (exact path recorded in Task 2's report — read it before starting).
- Produces: a `build` script that regenerates `src/schema.d.ts` from the live OpenAPI document; an exported, typed `apiClient` (from `openapi-fetch`) other packages could import later (none do yet — out of scope per the phase spec).

- [ ] **Step 1: Scaffold the package**

```bash
cd /Users/sufyan/Documents/Projects/st
mkdir -p packages/api-client/src
rm packages/api-client/.gitkeep
```

Create `packages/api-client/package.json`:

```json
{
  "name": "@st/api-client",
  "version": "0.0.0",
  "private": true,
  "type": "module",
  "main": "./src/client.ts",
  "types": "./src/client.ts",
  "scripts": {
    "build:openapi": "cd ../../apps/api && dotnet build Api.sln --nologo -v quiet && cd ../../packages/api-client && openapi-typescript ../../apps/api/openapi/Api.Host.json -o src/schema.d.ts",
    "build": "pnpm run build:openapi && tsc --noEmit",
    "check-types": "tsc --noEmit"
  },
  "dependencies": {
    "openapi-fetch": "^0.13.0"
  },
  "devDependencies": {
    "openapi-typescript": "^7.4.0",
    "typescript": "^5.7.0"
  }
}
```

(Adjust the `openapi-typescript`/`openapi-fetch`/`typescript` version ranges if `pnpm add` resolves different actual latest-stable versions — use whatever `pnpm add` picks, don't fight it to match these exact numbers. Adjust the `../../apps/api/openapi/Api.Host.json` path to whatever Task 2's report recorded as the REAL generated file path/name — this plan's assumption may not match reality.)

Create `packages/api-client/tsconfig.json` (check `tsconfig.base.json` at the repo root first and extend it, matching this monorepo's existing convention — look at `apps/web/tsconfig.json` for the pattern other packages use):

```json
{
  "extends": "../../tsconfig.base.json",
  "compilerOptions": {
    "outDir": "./dist",
    "rootDir": "./src"
  },
  "include": ["src"]
}
```

- [ ] **Step 2: Install dependencies**

```bash
cd /Users/sufyan/Documents/Projects/st
pnpm add openapi-fetch --filter @st/api-client
pnpm add -D openapi-typescript typescript --filter @st/api-client
```

- [ ] **Step 3: Generate the schema and write the client**

Run: `pnpm --filter @st/api-client run build:openapi`
Expected: `packages/api-client/src/schema.d.ts` is created with real, non-empty TypeScript type definitions derived from the OpenAPI document (open it and confirm it references `/{tenant-alias}/api/v1/tenant` and `/{tenant-alias}/api/v1/whoami`).

Create `packages/api-client/src/client.ts`:

```typescript
import createClient from "openapi-fetch";
import type { paths } from "./schema.d.ts";

export function createApiClient(baseUrl: string) {
  return createClient<paths>({ baseUrl });
}

export type { paths } from "./schema.d.ts";
```

(If `openapi-fetch`'s `createClient` generic signature or import path differs from this in the actual installed version, adjust to match — check the package's own type definitions/README if the compiler complains.)

- [ ] **Step 4: Verify the package builds**

Run: `pnpm --filter @st/api-client run build`
Expected: exits 0, no TypeScript errors (`tsc --noEmit` passes against the generated schema + `client.ts`).

- [ ] **Step 5: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add packages/api-client pnpm-lock.yaml
git commit -m "feat(api-client): generate TypeScript client from apps/api's OpenAPI document

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: SignalR hub

**Files:**
- Create: `apps/api/src/Api.Host/Hubs/TenantHub.cs`
- Modify: `apps/api/src/Api.Host/Program.cs` (`AddSignalR()`, `MapHub<TenantHub>(...)`)
- Modify: `apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj` (add `Microsoft.AspNetCore.SignalR.Client`)
- Create: `apps/api/tests/Api.Tests.Integration/TenantHubTests.cs`

**Interfaces:**
- Consumes: `CustomWebApplicationFactory`/`TestJwtTokenFactory` from Phase 3.
- Produces: a connectable SignalR hub at `/hubs/tenant`, requiring authentication. No hub methods beyond the SignalR framework defaults yet (ADR 0018/Phase 4 already noted no real domain-event-to-SignalR dispatcher exists — that's future work once something needs to push events to connected clients).

- [ ] **Step 1: Implement the hub**

Create `apps/api/src/Api.Host/Hubs/TenantHub.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Api.Host.Hubs;

[Authorize]
public sealed class TenantHub : Hub;
```

- [ ] **Step 2: Wire SignalR into `Program.cs`**

Read the current `apps/api/src/Api.Host/Program.cs`. Add `builder.Services.AddSignalR();` alongside the other service registrations. Add `app.MapHub<Api.Host.Hubs.TenantHub>("/hubs/tenant");` alongside the other endpoint mappings (after `app.UseAuthentication()`/`UseAuthorization()` are in place, since the hub is `[Authorize]`-protected).

- [ ] **Step 3: Add the SignalR test client package**

```bash
cd apps/api
dotnet add tests/Api.Tests.Integration/Api.Tests.Integration.csproj package Microsoft.AspNetCore.SignalR.Client
```

- [ ] **Step 4: Write the connection test**

Create `apps/api/tests/Api.Tests.Integration/TenantHubTests.cs`:

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantHubTests(PostgresContainerFixture fixture) : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new(fixture.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Connect_WithValidToken_ReachesConnectedState()
    {
        // Arrange
        var token = TestJwtTokenFactory.CreateToken($"clerk_hub_{Guid.NewGuid():N}");
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/tenant", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        // Act
        await connection.StartAsync();

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);

        // Cleanup
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Connect_WithoutToken_Fails()
    {
        // Arrange
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/tenant", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        await connection.DisposeAsync();
    }
}
```

If `_factory.Server` doesn't compile (`WebApplicationFactory<Program>` may need `.Server` accessed differently, or SignalR's in-memory-transport testing needs a different `HttpMessageHandlerFactory`/transport configuration in the installed package version — this is exactly the kind of thing the API-detail allowance covers), consult the actual `WebApplicationFactory<T>`/`Microsoft.AspNetCore.SignalR.Client` APIs available and adjust; document whatever working approach you land on. If genuinely blocked after real effort, it's acceptable to reduce this task's scope to a build-time-only proof (the hub compiles, is registered, `MapHub` doesn't throw at startup) plus a note explaining why a live connection test wasn't achievable — but attempt the real connection test first.

- [ ] **Step 5: Run the new tests**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~TenantHubTests"`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 6: Run the full solution and the pnpm gates**

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall.

Run (from repo root): `pnpm --filter api build`, `pnpm --filter api test`, `pnpm --filter @st/api-client build`
Expected: all succeed (phase spec acceptance criterion 6).

- [ ] **Step 7: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add SignalR TenantHub

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
