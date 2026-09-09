# API working rules

- Follow the root `AGENTS.md`.
- Target .NET 10 and PostgreSQL with EF Core code-first migrations.
- Keep the domain independent of ASP.NET Core, EF Core, and external services.
- Model business invariants in aggregates and value objects.
- Separate commands and queries; keep endpoints thin.
- Use EF Core by default; introduce Dapper for justified query needs.
- Avoid generic repositories and speculative abstractions.
- Store tenant business data in separate schemas; keep shared tenant, membership, invitation, and access-control records in the `admin` schema.
- Treat tenant aliases as mutable path identifiers. Use each tenant's immutable GUID in `N` format as its schema name and always quote it as a PostgreSQL identifier; never derive a schema name from an alias.
- Reserve system and platform aliases so tenants cannot claim protected routes or identities.
- Use Clerk only for authentication; manage authorization in the backend. Keep platform privileges separate from tenant roles.
- Tenant-scoped endpoints use `/{tenant-alias}/api/v{version}/...`.
- Resolve tenants from trusted control-plane data; fail closed on missing context.
- Enforce membership and permissions before executing tenant operations.
- Persist business changes and outbox messages atomically. Delivery may repeat; make outbox processing idempotent.
- Write a failing behavior test before implementing business logic or fixing bugs.
- Use xUnit with explicit `// Arrange`, `// Act`, and `// Assert` sections.
- Test persistence and tenant isolation against real PostgreSQL with Testcontainers.
- Keep API contracts versioned and generate shared client types from OpenAPI.
- Do not add comments that repeat the code; prefer clear names. Only explain non-obvious constraints or rationale, apart from required AAA labels in tests.
- Run affected checks and report results.

## Commands

Run from `apps/api`:

- Build: `dotnet build Api.slnx`
- Test: `dotnet test --solution Api.slnx`
- Start API: `dotnet run --project src/Api --urls http://localhost:5000`
- Restore EF tool: `dotnet tool restore`
- Add admin migration: `dotnet tool run dotnet-ef migrations add <Name> --project src/Infrastructure --startup-project src/Api --context AdminDbContext --output-dir Persistence/Admin/Migrations`

- `/health` checks application liveness only.
- `/health/ready` checks PostgreSQL readiness and requires `ConnectionStrings__Postgres`.
