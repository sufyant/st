# apps/api Phase 1: .NET Solution Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a working, compiling, `pnpm dev`-able .NET 10 solution skeleton in `apps/api`, with a `/health` endpoint and a passing test, that later phases build real functionality on top of.

**Architecture:** Layered solution (`Api.Domain` -> `Api.Application` -> `Api.Infrastructure` -> `Api.Host`, each referencing only the layer inside it), plus `Api.Tests.Unit`. No business logic yet — this phase proves the skeleton compiles, runs, and is wired into the pnpm/Turborepo monorepo.

**Tech Stack:** .NET 10 SDK, ASP.NET Core (minimal APIs), xUnit, `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory).

## Global Constraints

- SDK pinned to .NET 10 via `apps/api/global.json` — per [ADR 0028](../../adr/0028-dotnet-10-runtime.md), never fall back to the 9.0.302 SDK also installed on this machine.
- All new `.csproj` files target `net10.0` explicitly.
- `apps/api/package.json` is a thin wrapper only (no real Node dependency) — `dev`/`build`/`test` scripts shell out to `dotnet`, per [ADR 0029](../../adr/0029-monorepo-tooling.md).
- Delete generated template stub files that carry no real content (`Class1.cs`, the default `UnitTest1.cs`) — don't leave placeholder boilerplate in the tree.
- No domain/business logic in this phase — that's Phase 2 (`docs/superpowers/plans/2026-09-07-apps-api-phases-ledger.md`).
- Work from `/Users/sufyan/Documents/Projects/st` (repo root); all `dotnet` commands below are run with `cd apps/api &&` unless noted.

---

### Task 1: Solution and project skeleton

**Files:**
- Create: `apps/api/global.json`
- Create: `apps/api/Api.sln`
- Create: `apps/api/src/Api.Domain/Api.Domain.csproj`
- Create: `apps/api/src/Api.Application/Api.Application.csproj`
- Create: `apps/api/src/Api.Infrastructure/Api.Infrastructure.csproj`
- Create: `apps/api/src/Api.Host/Api.Host.csproj`
- Create: `apps/api/src/Api.Host/Program.cs` (template default, edited in Task 2)
- Create: `apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj`
- Delete: `apps/api/.gitkeep` (directory is no longer empty)

**Interfaces:**
- Consumes: nothing (first task).
- Produces: a solution (`Api.sln`) containing 5 projects with reference chain `Api.Host -> Api.Infrastructure -> Api.Application -> Api.Domain`, and `Api.Tests.Unit -> Api.Host`. Task 2 adds real code to `Api.Host/Program.cs` and `Api.Tests.Unit`.

- [ ] **Step 1: Create global.json pinning the SDK**

```bash
cd apps/api
cat > global.json << 'EOF'
{
  "sdk": {
    "version": "10.0.102",
    "rollForward": "latestFeature"
  }
}
EOF
```

- [ ] **Step 2: Create the solution file**

```bash
dotnet new sln -n Api
```

- [ ] **Step 3: Scaffold the four layer projects and the test project**

```bash
dotnet new classlib -n Api.Domain -o src/Api.Domain
dotnet new classlib -n Api.Application -o src/Api.Application
dotnet new classlib -n Api.Infrastructure -o src/Api.Infrastructure
dotnet new web -n Api.Host -o src/Api.Host
dotnet new xunit -n Api.Tests.Unit -o tests/Api.Tests.Unit
```

- [ ] **Step 4: Remove template stub files with no real content**

```bash
rm src/Api.Domain/Class1.cs
rm src/Api.Application/Class1.cs
rm src/Api.Infrastructure/Class1.cs
rm tests/Api.Tests.Unit/UnitTest1.cs
```

- [ ] **Step 5: Wire the project reference chain**

```bash
dotnet add src/Api.Application/Api.Application.csproj reference src/Api.Domain/Api.Domain.csproj
dotnet add src/Api.Infrastructure/Api.Infrastructure.csproj reference src/Api.Application/Api.Application.csproj
dotnet add src/Api.Host/Api.Host.csproj reference src/Api.Infrastructure/Api.Infrastructure.csproj
dotnet add tests/Api.Tests.Unit/Api.Tests.Unit.csproj reference src/Api.Host/Api.Host.csproj
```

- [ ] **Step 6: Add all projects to the solution**

```bash
dotnet sln Api.sln add src/Api.Domain/Api.Domain.csproj src/Api.Application/Api.Application.csproj src/Api.Infrastructure/Api.Infrastructure.csproj src/Api.Host/Api.Host.csproj tests/Api.Tests.Unit/Api.Tests.Unit.csproj
```

- [ ] **Step 7: Confirm every csproj targets net10.0**

Run: `grep -L '<TargetFramework>net10.0</TargetFramework>' src/*/*.csproj tests/*/*.csproj`
Expected: empty output (every file already contains that line — `dotnet new` picks it up from `global.json`'s pinned SDK). If any file is listed, edit its `<TargetFramework>` element to `net10.0` by hand.

- [ ] **Step 8: Remove the now-obsolete placeholder**

Still inside `apps/api` (from Step 1's `cd`):

```bash
rm .gitkeep
```

- [ ] **Step 9: Build the solution**

Run: `dotnet build Api.sln`
Expected: `Build succeeded.` — 5 projects compiled (Tests.Unit has zero test files at this point, which is fine; it still builds).

- [ ] **Step 10: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): bootstrap .NET 10 solution skeleton

Api.Domain -> Api.Application -> Api.Infrastructure -> Api.Host layer
chain plus Api.Tests.Unit, no business logic yet.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: `/health` endpoint with a real test

**Files:**
- Modify: `apps/api/src/Api.Host/Program.cs`
- Create: `apps/api/tests/Api.Tests.Unit/HealthEndpointTests.cs`
- Modify: `apps/api/tests/Api.Tests.Unit/Api.Tests.Unit.csproj` (adds `Microsoft.AspNetCore.Mvc.Testing` package reference)

**Interfaces:**
- Consumes: the `Api.Host` project and its `Program` entry point from Task 1.
- Produces: `GET /health` returning `200 OK` with JSON body `{"status":"ok"}`; a `public partial class Program` marker other test classes in later phases can also target via `WebApplicationFactory<Program>`.

- [ ] **Step 1: Add the WebApplicationFactory test package**

```bash
cd apps/api
dotnet add tests/Api.Tests.Unit/Api.Tests.Unit.csproj package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 2: Write the failing test**

Create `tests/Api.Tests.Unit/HealthEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Api.Tests.Unit;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsOkWithStatusOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body?.Status);
    }

    private sealed record HealthResponse(string Status);
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Api.Tests.Unit/Api.Tests.Unit.csproj`
Expected: build error `CS0122: 'Program' is inaccessible due to its protection level` (the template's top-level-statements `Program.cs` has no accessible `Program` type yet for `WebApplicationFactory<Program>` to target).

- [ ] **Step 4: Implement the minimal endpoint**

Replace the full contents of `src/Api.Host/Program.cs` with:

```csharp
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Api.Tests.Unit/Api.Tests.Unit.csproj`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
cd /Users/sufyan/Documents/Projects/st
git add apps/api
git commit -m "feat(api): add /health endpoint with WebApplicationFactory test

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Monorepo wiring (pnpm/Turborepo)

**Files:**
- Create: `apps/api/package.json`

**Interfaces:**
- Consumes: `dotnet build`/`dotnet test`/`dotnet watch` from Tasks 1-2; the repo-root `turbo.json` task definitions (`dev`, `build`, `test`) already in place — unchanged by this task.
- Produces: `pnpm --filter api dev|build|test` (and, from repo root, `pnpm dev`/`pnpm build`/`pnpm test` via Turborepo) working end-to-end for `apps/api`.

- [ ] **Step 1: Write the thin package.json wrapper**

Create `apps/api/package.json`:

```json
{
  "name": "api",
  "private": true,
  "scripts": {
    "dev": "dotnet watch --project src/Api.Host run",
    "build": "dotnet build Api.sln",
    "test": "dotnet test Api.sln"
  }
}
```

- [ ] **Step 2: Verify pnpm picks up the new workspace package**

Run (from repo root): `pnpm install`
Expected: exits 0, `api` listed as a workspace package (pnpm re-links the workspace; no new external dependencies are added since `package.json` declares none).

- [ ] **Step 3: Verify build via Turborepo**

Run (from repo root): `pnpm --filter api build`
Expected: `Build succeeded.` (same output as Task 1 Step 9, now invoked through pnpm/Turborepo).

- [ ] **Step 4: Verify test via Turborepo**

Run (from repo root): `pnpm --filter api test`
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0` (the health-endpoint test from Task 2, now invoked through pnpm/Turborepo).

- [ ] **Step 5: Verify the dev server boots and serves /health, then stop it**

Run (from repo root, backgrounded): `pnpm --filter api dev &`
Wait ~5s for `dotnet watch` to finish its first build and bind the port (check its output for `Now listening on: http://localhost:<port>`), then:
Run: `curl -s http://localhost:<port>/health` (substitute the port printed in the previous output)
Expected: `{"status":"ok"}`
Then stop the backgrounded dev server (e.g. `kill %1` or the job's PID) — do not leave it running.

- [ ] **Step 6: Commit**

```bash
git add apps/api/package.json pnpm-lock.yaml
git commit -m "feat(api): wire apps/api into pnpm/Turborepo (ADR 0029)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
