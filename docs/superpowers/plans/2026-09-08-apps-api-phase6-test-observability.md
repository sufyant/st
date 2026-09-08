# apps/api Phase 6: Test + Observability Infra Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Serilog + Seq structured logging with automatic tenant_id/request_id/user_id enrichment on every mediator-dispatched request (ADR 0026), and a dedicated `Api.Tests.TenantIsolation` project that is the single, canonical answer to "is tenant isolation tested?" (ADR 0025).

**Architecture:** `LoggingBehavior` moves from `Api.Application` to `Api.Infrastructure` (it needs `IHttpContextAccessor` to read claims, and `Api.Application` is deliberately kept free of ASP.NET Core dependencies — this is the same reason `PermissionBehavior` already lives in `Api.Infrastructure` instead of `Api.Application`). It pushes tenant_id/request_id/user_id onto Serilog's ambient `LogContext` for the duration of each `Send()` call, so every log line written during that call — by any component, not just the behavior itself — carries those fields. Two existing pieces of Testcontainers-based test infrastructure (`PostgresContainerFixture`, `CustomWebApplicationFactory`, `TestJwtTokenFactory`) are extracted from `Api.Tests.Integration` into a new shared class library, `Api.Tests.Shared`, so the new `Api.Tests.TenantIsolation` project can reuse them instead of duplicating ~90 lines of container/host/JWT plumbing.

**Tech Stack:** `Serilog.AspNetCore`, `Serilog.Sinks.Seq`, `Serilog.Sinks.Console` (Api.Host); `Serilog` (core, for `LogContext` — Api.Infrastructure).

## Global Constraints

- `LoggingBehavior` enrichment covers only mediator-dispatched requests (currently just `RenameTenantCommand`) — non-mediator endpoints (`/health`, `/whoami`) are explicitly out of scope this phase, per ADR 0026's literal text ("pipeline behavior zincirinin bir parçası olarak").
- The new `Api.Tests.TenantIsolation` tests intentionally overlap in what they assert with existing tests in `Api.Tests.Integration` (e.g. cross-tenant write rejection is tested in both `RenameTenantCommandEndToEndTests`/`RenameTenantEndpointTests` AND here). This is plan-mandated, not an oversight — ADR 0025's whole point is a single, dedicated, obviously-named place that on its own proves tenant isolation holds. Do not flag this overlap as unwanted duplication; do not delete or modify the existing tests in `Api.Tests.Integration`.
- No new business functionality. No `TenantDbContext` product entities. No log alerting/dashboards beyond Seq's own UI (out of the box).
- Work from `/Users/sufyan/Documents/Projects/st` (repo root). Docker must be running for integration tests.
- API-detail allowance applies to: the exact Serilog/Serilog.Sinks.Seq package API surface (`UseSerilog` overload signatures, `LoggerConfiguration` extension method names) — these have had minor signature changes across versions, adjust to whatever the installed version actually exposes and document it; and to the exact mechanism for asserting captured log output in Task 2's enrichment test (a hand-rolled in-memory `ILogEventSink` is specified below as the default approach — deviate only if it proves impractical, and document why).

---

### Task 1: Extract shared test infrastructure into `Api.Tests.Shared`

**Files:**
- Create: `apps/api/tests/Api.Tests.Shared/Api.Tests.Shared.csproj`
- Move: `apps/api/tests/Api.Tests.Integration/PostgresContainerFixture.cs` → `apps/api/tests/Api.Tests.Shared/PostgresContainerFixture.cs`
- Move: `apps/api/tests/Api.Tests.Integration/CustomWebApplicationFactory.cs` → `apps/api/tests/Api.Tests.Shared/CustomWebApplicationFactory.cs`
- Move: `apps/api/tests/Api.Tests.Integration/TestJwtTokenFactory.cs` → `apps/api/tests/Api.Tests.Shared/TestJwtTokenFactory.cs`
- Modify: `apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj` (add project reference, drop now-duplicate package references those 3 files needed but nothing else in the project needs — check carefully, most are still needed by other files)
- Modify: every `.cs` file under `apps/api/tests/Api.Tests.Integration/` that references `PostgresContainerFixture`, `PostgresCollection`, `CustomWebApplicationFactory`, or `TestJwtTokenFactory` (add `using Api.Tests.Shared;`)
- Modify: `apps/api/Api.sln` (add the new project)

**Interfaces:**
- Consumes: nothing new — this is a pure relocation of Phase 2b/3's existing test infrastructure.
- Produces: `Api.Tests.Shared.PostgresContainerFixture`, `Api.Tests.Shared.PostgresCollection`, `Api.Tests.Shared.CustomWebApplicationFactory`, `Api.Tests.Shared.TestJwtTokenFactory` — same public API as before, new namespace (`Api.Tests.Shared` instead of `Api.Tests.Integration`), consumed by both `Api.Tests.Integration` (existing) and `Api.Tests.TenantIsolation` (Task 3).

- [ ] **Step 1: Read the three files being moved and their current namespace**

Read `apps/api/tests/Api.Tests.Integration/PostgresContainerFixture.cs`, `CustomWebApplicationFactory.cs`, and `TestJwtTokenFactory.cs` in full before moving anything — confirm their current `namespace Api.Tests.Integration;` declarations and exact public surface (you already have this from the plan's own research, but re-verify against the live files since other phases may have touched them since).

- [ ] **Step 2: Create the new project**

```bash
cd /Users/sufyan/Documents/Projects/st/apps/api/tests
mkdir Api.Tests.Shared
```

Create `apps/api/tests/Api.Tests.Shared/Api.Tests.Shared.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.11" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.22.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.15.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Api.Infrastructure\Api.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\Api.Host\Api.Host.csproj" />
  </ItemGroup>

</Project>
```

(Package versions should match whatever `Api.Tests.Integration.csproj` currently has for the same packages — read it first and copy the exact version numbers rather than trusting the numbers above, which are this plan's best-effort guess. `xunit` is needed here only for the `[CollectionDefinition]`/`ICollectionFixture<T>`/`IAsyncLifetime` attributes `PostgresContainerFixture`/`PostgresCollection` use — this project has no `[Fact]`s of its own, so it does not need `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, or `coverlet.collector`.)

- [ ] **Step 3: Move the three files and change their namespace**

```bash
git mv apps/api/tests/Api.Tests.Integration/PostgresContainerFixture.cs apps/api/tests/Api.Tests.Shared/PostgresContainerFixture.cs
git mv apps/api/tests/Api.Tests.Integration/CustomWebApplicationFactory.cs apps/api/tests/Api.Tests.Shared/CustomWebApplicationFactory.cs
git mv apps/api/tests/Api.Tests.Integration/TestJwtTokenFactory.cs apps/api/tests/Api.Tests.Shared/TestJwtTokenFactory.cs
```

In all three moved files, change `namespace Api.Tests.Integration;` to `namespace Api.Tests.Shared;`. Do not change anything else in these files — this is a pure move + rename, not a rewrite.

- [ ] **Step 4: Wire up references**

Add to `apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj`'s `<ItemGroup>` containing `<ProjectReference>`:

```xml
<ProjectReference Include="..\Api.Tests.Shared\Api.Tests.Shared.csproj" />
```

Read the rest of `Api.Tests.Integration.csproj`'s `<PackageReference>` list and remove only the packages that were needed exclusively by the three moved files and are not referenced by anything remaining in `Api.Tests.Integration` (check with `grep -rl` across the remaining `.cs` files for each package's types before removing anything — e.g. `Microsoft.AspNetCore.SignalR.Client` is used by `TenantHubTests.cs`, not by the moved files, so it stays regardless).

Add `using Api.Tests.Shared;` to every remaining `.cs` file in `apps/api/tests/Api.Tests.Integration/` that references any of the four moved types. Find them with:

```bash
grep -rl "PostgresContainerFixture\|PostgresCollection\|CustomWebApplicationFactory\|TestJwtTokenFactory" apps/api/tests/Api.Tests.Integration/*.cs
```

- [ ] **Step 5: Add the new project to the solution**

```bash
cd /Users/sufyan/Documents/Projects/st/apps/api
dotnet sln Api.sln add tests/Api.Tests.Shared/Api.Tests.Shared.csproj
```

- [ ] **Step 6: Build and run the full existing suite to confirm nothing broke**

Run: `dotnet build apps/api/Api.sln`
Expected: builds clean, no errors.

Run: `dotnet test apps/api/Api.sln`
Expected: same test count and pass rate as before this task (134 tests, all passing) — this task moves code, it does not add or remove any test.

- [ ] **Step 7: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "refactor(api): extract shared test infra into Api.Tests.Shared

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Serilog + Seq, LoggingBehavior enrichment and relocation

**Files:**
- Create: `apps/api/src/Api.Infrastructure/LoggingBehavior.cs`
- Delete: `apps/api/src/Api.Application/LoggingBehavior.cs`
- Modify: `apps/api/src/Api.Infrastructure/Api.Infrastructure.csproj` (add `Serilog` package)
- Modify: `apps/api/src/Api.Host/Api.Host.csproj` (add `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Serilog.Sinks.Seq`)
- Modify: `apps/api/src/Api.Host/Program.cs` (`UseSerilog`, update the `LoggingBehavior` DI registration's namespace)
- Modify: `apps/api/src/Api.Host/appsettings.json` (or create `appsettings.Development.json` if that's the established pattern — check what exists first) — add `Seq:ServerUrl`.
- Create: `apps/api/tests/Api.Tests.Integration/LoggingEnrichmentTests.cs`

**Interfaces:**
- Consumes: `IHttpContextAccessor` (already DI-registered since Phase 4), `RenameTenantCommand`/`IMediator` (Phase 4/5) as the concrete mediator-dispatched request to prove enrichment against.
- Produces: `Api.Infrastructure.LoggingBehavior<,>` (replaces `Api.Application.LoggingBehavior<,>` in DI registration and in the pipeline — same position, outermost). Every log line written during a `Send()` call to a request carries `tenant_id`/`request_id`/`user_id` Serilog properties (`tenant_id`/`user_id` are `null`/absent when the caller is unauthenticated or the request has no `ITenantScopedRequest`/claims to read — this behavior must not throw for anonymous or non-tenant-scoped requests).

- [ ] **Step 1: Read the current `LoggingBehavior.cs` and `PermissionBehavior.cs`**

Read `apps/api/src/Api.Application/LoggingBehavior.cs` and `apps/api/src/Api.Infrastructure/PermissionBehavior.cs` in full — the new file should follow `PermissionBehavior`'s exact pattern for injecting and reading from `IHttpContextAccessor`/`ClaimsPrincipal`.

- [ ] **Step 2: Add the Serilog package to `Api.Infrastructure`**

```bash
cd /Users/sufyan/Documents/Projects/st/apps/api
dotnet add src/Api.Infrastructure/Api.Infrastructure.csproj package Serilog
```

(This is the core `Serilog` package only — for `Serilog.Context.LogContext`. No sinks needed here; sinks are an `Api.Host`-only concern.)

- [ ] **Step 3: Write the relocated, enriching `LoggingBehavior`**

Delete `apps/api/src/Api.Application/LoggingBehavior.cs`.

Create `apps/api/src/Api.Infrastructure/LoggingBehavior.cs`:

```csharp
using System.Security.Claims;
using Api.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Api.Infrastructure;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger, IHttpContextAccessor httpContextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        var tenantId = user?.FindFirstValue("tenant_id");
        var userId = user?.FindFirstValue("sub");
        var requestId = httpContext?.TraceIdentifier;

        using (LogContext.PushProperty("tenant_id", tenantId))
        using (LogContext.PushProperty("request_id", requestId))
        using (LogContext.PushProperty("user_id", userId))
        {
            logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);
            var response = await next();
            logger.LogInformation("Handled {RequestName}", typeof(TRequest).Name);
            return response;
        }
    }
}
```

(`LogContext.PushProperty` accepts a `null` value without throwing — Serilog simply omits or nulls the property in that case, so no extra null-guarding is needed for anonymous/non-tenant requests. Verify this is still true for the installed Serilog version by checking the package's own XML docs/source if you have doubts, and adjust if it actually throws.)

- [ ] **Step 4: Add Serilog packages to `Api.Host`**

```bash
cd /Users/sufyan/Documents/Projects/st/apps/api
dotnet add src/Api.Host/Api.Host.csproj package Serilog.AspNetCore
dotnet add src/Api.Host/Api.Host.csproj package Serilog.Sinks.Console
dotnet add src/Api.Host/Api.Host.csproj package Serilog.Sinks.Seq
```

(`Serilog.AspNetCore` brings in core `Serilog` + `Serilog.Extensions.Hosting` transitively — confirm this after `dotnet add` rather than assuming; add `Serilog.Sinks.Console`/`Serilog.Sinks.Seq` explicitly regardless since they're sinks, not part of the hosting integration.)

- [ ] **Step 5: Wire `UseSerilog` into `Program.cs`**

Read the current `apps/api/src/Api.Host/Program.cs` in full first. Add, immediately after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
builder.Host.UseSerilog((context, services, configuration) => configuration
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341"));
```

(`.Enrich.FromLogContext()` is what makes `LoggingBehavior`'s `LogContext.PushProperty` calls actually show up on emitted log events — without it the pushed properties are silently ignored. If the exact `UseSerilog` overload signature or fluent API differs in the installed package version, adjust to what compiles and document the real signature used in your report.)

Update the DI registration further down in `Program.cs`:

```csharp
builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Application.LoggingBehavior<,>));
```

→

```csharp
builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Infrastructure.LoggingBehavior<,>));
```

(Keep this registration in the exact same position in the list — it must remain first/outermost, per the existing `// Registration order` comment above it.)

- [ ] **Step 6: Configure the Seq URL**

Read `apps/api/src/Api.Host/appsettings.json` (and `appsettings.Development.json` if it exists) to see the established config-file convention (compare to how `Clerk:Authority`/`ConnectionStrings:AdminDb` are already configured). Add a `Seq` section following the same pattern:

```json
{
  "Seq": {
    "ServerUrl": "http://localhost:5341"
  }
}
```

(This is also the hardcoded fallback in Step 5's `??`, so an entirely missing config section still works — add the explicit config entry anyway for discoverability/overridability, matching how `ConnectionStrings:AdminDb` is already explicit rather than relying on a hardcoded default.)

- [ ] **Step 7: Write the enrichment test**

Read `apps/api/tests/Api.Tests.Integration/RenameTenantEndpointTests.cs` in full first — this new test reuses its exact tenant/user/membership provisioning pattern (via `Api.Tests.Shared.CustomWebApplicationFactory`/`TestJwtTokenFactory` after Task 1's move) to dispatch a real `RenameTenantCommand` over HTTP, then asserts on captured Serilog output.

Create `apps/api/tests/Api.Tests.Integration/LoggingEnrichmentTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public sealed class LoggingEnrichmentTests : IDisposable
{
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly CapturingSink _sink = new();
    private readonly Logger _testLogger;

    public LoggingEnrichmentTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
        _testLogger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(_sink).CreateLogger();
        Log.Logger = _testLogger;
        _factory = new CustomWebApplicationFactory(fixture.ConnectionString);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _testLogger.Dispose();
    }

    [Fact]
    public async Task RenameTenantCommand_EmitsLogsEnrichedWithTenantAndUserAndRequestId()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(_fixture.ConnectionString).Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"log-{Guid.NewGuid():N}"[..15]), "Original Name");

        var clerkUserId = $"clerk_log_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenant.Id, "owner"));
        dbContext.RolePermissions.Add(RolePermission.Create("owner", "tenant.rename"));
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        await client.PutAsJsonAsync($"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "Renamed" });

        // Assert
        Assert.Contains(_sink.Events, e =>
            e.Properties.TryGetValue("tenant_id", out var tenantIdProp)
            && tenantIdProp.ToString().Contains(tenant.Id.ToString())
            && e.Properties.ContainsKey("request_id")
            && e.Properties.TryGetValue("user_id", out var userIdProp)
            && userIdProp.ToString().Contains(clerkUserId));
    }
}
```

This test relies on `Log.Logger` being a process-wide static — read `Program.cs`'s actual `UseSerilog` call and confirm whether the app host writes through `Log.Logger` (Serilog's default static sink dispatch, unless a fully DI-scoped logger provider is configured instead) or through a purely DI-injected `ILogger<T>` that never touches `Log.Logger`. If `UseSerilog`'s `(context, services, configuration)` overload keeps everything DI-scoped and never touches the static `Log.Logger`, this test's approach of swapping `Log.Logger` won't observe the app's real log output — in that case, adjust the test to instead override the sink via `CustomWebApplicationFactory`'s `ConfigureWebHost` (call `builder.UseSerilog(...)` again inside `ConfigureServices`/`ConfigureWebHost` to append `.WriteTo.Sink(_sink)` for this specific factory instance) rather than mutating the global static logger, which is the safer and more idiomatic approach for a test that must not leak state across parallel test classes. Use your judgment on which approach genuinely works against the installed Serilog.AspNetCore version — this is exactly the kind of API-detail deviation this plan's Global Constraints anticipate. Document whichever approach you land on and why in your report.

- [ ] **Step 8: Run the new test and the full suite**

Run: `dotnet test apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj --filter "FullyQualifiedName~LoggingEnrichmentTests"`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall (134 pre-existing + 1 new = 135; recount from actual output).

- [ ] **Step 9: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): Serilog + Seq structured logging with tenant/request/user enrichment

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: `Api.Tests.TenantIsolation` — the dedicated tenant-isolation test project

**Files:**
- Create: `apps/api/tests/Api.Tests.TenantIsolation/Api.Tests.TenantIsolation.csproj`
- Create: `apps/api/tests/Api.Tests.TenantIsolation/SchemaIsolationTests.cs`
- Create: `apps/api/tests/Api.Tests.TenantIsolation/CrossTenantMembershipTests.cs`
- Create: `apps/api/tests/Api.Tests.TenantIsolation/CrossTenantWriteTests.cs`
- Create: `apps/api/tests/Api.Tests.TenantIsolation/JwtClaimInjectionTests.cs`
- Modify: `apps/api/Api.sln` (add the new project)

**Interfaces:**
- Consumes: `Api.Tests.Shared.PostgresContainerFixture`/`PostgresCollection`/`CustomWebApplicationFactory`/`TestJwtTokenFactory` (Task 1), `TenantProvisioningService`/`AdminDbContext`/`Tenant`/`User`/`Membership`/`RolePermission`/`TenantSlug`/`Email` (Phase 2b), `TenantResolutionMiddleware`'s HTTP behavior via `/{tenant-alias}/api/v1/whoami` and `/{tenant-alias}/api/v1/tenant` (Phase 3/5).
- Produces: nothing consumed by later tasks — this is the final task of the final phase.

- [ ] **Step 1: Read the reference tests this project's tests are modeled on**

Read in full: `apps/api/tests/Api.Tests.Integration/RenameTenantEndpointTests.cs` (Phase 5, cross-tenant write / provisioning pattern), `apps/api/tests/Api.Tests.Integration/TenantResolutionMiddlewareTests.cs` (Phase 3, JWT claim-injection pattern — specifically the tests using `extraClaims:` with `tenant_id`/`permission`/`membership_role`), and `apps/api/src/Api.Infrastructure/TenantProvisioningService.cs` (exact `ProvisionAsync` signature).

- [ ] **Step 2: Create the project**

```bash
cd /Users/sufyan/Documents/Projects/st/apps/api/tests
mkdir Api.Tests.TenantIsolation
```

Create `apps/api/tests/Api.Tests.TenantIsolation/Api.Tests.TenantIsolation.csproj` — copy `apps/api/tests/Api.Tests.Integration/Api.Tests.Integration.csproj`'s structure exactly (same package versions — read the live file, this plan doesn't repeat every version number to avoid drift risk) but with these differences: no `Microsoft.AspNetCore.SignalR.Client` package reference (not needed — this project has no SignalR tests), and an added `<ProjectReference Include="..\Api.Tests.Shared\Api.Tests.Shared.csproj" />` alongside the existing `Api.Infrastructure`/`Api.Host` references.

- [ ] **Step 3: Schema isolation test**

Create `apps/api/tests/Api.Tests.TenantIsolation/SchemaIsolationTests.cs`:

```csharp
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Api.Tests.TenantIsolation;

[Collection(nameof(PostgresCollection))]
public sealed class SchemaIsolationTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task TwoProvisionedTenants_GetTwoDistinctPostgresSchemas()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);

        var tenantA = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-a-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var tenantB = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-b-{Guid.NewGuid():N}"[..15]), "Tenant B");

        // Act
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_name FROM information_schema.schemata WHERE schema_name = ANY(@names)";
        command.Parameters.AddWithValue("names", new[] { $"tenant_{tenantA.Slug.Value}", $"tenant_{tenantB.Slug.Value}" });
        var foundSchemas = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            foundSchemas.Add(reader.GetString(0));
        }

        // Assert
        Assert.Equal(2, foundSchemas.Count);
        Assert.NotEqual(foundSchemas[0], foundSchemas[1]);
    }
}
```

(Confirm the exact schema-naming convention — `tenant_{slug}` — against `TenantProvisioningService.cs`'s actual implementation before trusting this plan's assumption; adjust the query if it differs. Confirm whether `Npgsql` needs an explicit `PackageReference` in this project — `Testcontainers.PostgreSql`/`Microsoft.EntityFrameworkCore.Relational`'s Npgsql provider likely already pulls it in transitively; add an explicit reference only if the build fails without one.)

- [ ] **Step 4: Cross-tenant membership test**

Create `apps/api/tests/Api.Tests.TenantIsolation/CrossTenantMembershipTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.TenantIsolation;

[Collection(nameof(PostgresCollection))]
public sealed class CrossTenantMembershipTests(PostgresContainerFixture fixture) : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new(fixture.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task UserWithMembershipInTenantA_CannotAccessTenantB()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenantA = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-mem-a-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var tenantB = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-mem-b-{Guid.NewGuid():N}"[..15]), "Tenant B");

        var clerkUserId = $"clerk_iso_mem_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenantA.Id, "owner"));
        dbContext.RolePermissions.Add(RolePermission.Create("owner", "tenant.whoami"));
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/{tenantB.Slug.Value}/api/v1/whoami");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

(If a prior test elsewhere already inserted a `("owner", "tenant.whoami")` `RolePermission` row and the unique index rejects this duplicate insert on the shared test database, use the check-then-add pattern already established in `PermissionAuthorizationTests.cs`/`RenameTenantEndpointTests.cs` — read one of those files' handling of this exact situation and reuse it here too, rather than assuming a bare `Add` is safe.)

- [ ] **Step 5: Cross-tenant write test**

Create `apps/api/tests/Api.Tests.TenantIsolation/CrossTenantWriteTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.TenantIsolation;

[Collection(nameof(PostgresCollection))]
public sealed class CrossTenantWriteTests(PostgresContainerFixture fixture) : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new(fixture.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task AuthenticatedForTenantA_CannotRenameTenantB()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenantA = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-wr-a-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var tenantB = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-wr-b-{Guid.NewGuid():N}"[..15]), "Tenant B Original");

        var clerkUserId = $"clerk_iso_wr_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenantA.Id, "owner"));
        dbContext.RolePermissions.Add(RolePermission.Create("owner", "tenant.rename"));
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act: the caller is authenticated FOR tenant A (the URL prefix), attempting to rename
        // tenant B is not directly expressible through this endpoint's own request shape (it
        // reads the target tenant id from the authenticated tenant_id claim, not the body) —
        // this test instead proves the isolation guarantee at the layer where it's actually
        // enforced: PermissionBehavior's ITenantScopedRequest check, exercised by attempting to
        // rename tenant A's own URL while asserting tenant B's data is provably untouched.
        var response = await client.PutAsJsonAsync(
            $"/{tenantA.Slug.Value}/api/v1/tenant", new { NewName = "Renamed A" });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verifyContext = new AdminDbContext(options);
        var reloadedB = await verifyContext.Tenants.SingleAsync(t => t.Id == tenantB.Id);
        Assert.Equal("Tenant B Original", reloadedB.Name);
    }
}
```

**Before implementing this test as written above: reconsider it.** The plan's own comment inside the test body above admits the scenario doesn't directly exercise a cross-tenant attempt — because the endpoint structurally can't receive a client-supplied target tenant id (that's the Phase 5 design decision that closed this bug class at the routing layer). A more faithful test of the actual defense-in-depth mechanism (`ITenantScopedRequest` in `PermissionBehavior`) requires dispatching `RenameTenantCommand` directly via `IMediator.Send(...)` with a mismatched `TenantId`, bypassing the HTTP layer — similar to how Phase 4's own `RenameTenantCommandEndToEndTests` likely already does this (read it, it's referenced in the ledger). Read `apps/api/tests/Api.Tests.Integration/RenameTenantCommandEndToEndTests.cs` in full, and write this test to mirror its direct-mediator-dispatch approach instead of (or in addition to) the HTTP-level version sketched above, so it actually proves the `ITenantScopedRequest`/`Tenant.Mismatch` rejection path. Use your judgment on the single clearest test to write here; the plan's sketch above is a starting point, not a mandate if it turns out not to prove what it claims.

- [ ] **Step 6: JWT claim injection test**

Create `apps/api/tests/Api.Tests.TenantIsolation/JwtClaimInjectionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
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

    [Fact]
    public async Task ForgedTenantIdClaimInJwt_CannotOverrideDbDerivedTenantContext()
    {
        // Arrange: the caller has real membership in tenantA. Their JWT carries a forged
        // "tenant_id" claim pointing at a different, real tenantB. If TenantResolutionMiddleware
        // failed to strip pre-existing claims of these types before adding its own DB-derived
        // ones, ClaimsPrincipal.FindFirst("tenant_id") could return the forged value instead.
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenantA = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-jwt-a-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var tenantB = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"iso-jwt-b-{Guid.NewGuid():N}"[..15]), "Tenant B");

        var clerkUserId = $"clerk_iso_jwt_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenantA.Id, "owner"));
        dbContext.RolePermissions.Add(RolePermission.Create("owner", "tenant.whoami"));
        await dbContext.SaveChangesAsync();

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
```

(Add `using System.Text.Json;` for `JsonElement` if not already implied by an existing `using`. If a prior test elsewhere already inserted a `("owner", "tenant.whoami")` row, use the check-then-add pattern per Step 4's note.)

- [ ] **Step 7: Add the project to the solution**

```bash
cd /Users/sufyan/Documents/Projects/st/apps/api
dotnet sln Api.sln add tests/Api.Tests.TenantIsolation/Api.Tests.TenantIsolation.csproj
```

- [ ] **Step 8: Run the new tests and the full suite**

Run: `dotnet test apps/api/tests/Api.Tests.TenantIsolation/Api.Tests.TenantIsolation.csproj`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

Run: `dotnet test apps/api/Api.sln`
Expected: `Failed: 0` overall (recount from actual output).

Run (from repo root): `pnpm --filter api build`, `pnpm --filter api test`
Expected: both succeed (phase acceptance criterion 4).

- [ ] **Step 9: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "test(api): add dedicated Api.Tests.TenantIsolation project

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
