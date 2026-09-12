# API working rules

- Follow the root `AGENTS.md`.
- Target .NET 10 and PostgreSQL with EF Core code-first migrations.
- Keep the domain independent of ASP.NET Core, EF Core, and external services.
- Model business invariants in aggregates and value objects.
- Separate commands and queries; an endpoint maps HTTP to a request and a result to a status code, nothing else.
- Keep a vertical slice in two folders of the same name: `src/Application/Features/<Feature>/` for the request, validator, handler and response, one slice per file; `src/Api/Features/<Feature>/` for the routes. No `Commands/`, `Handlers/` or `Validators/` folders.
- References run `Domain <- Infrastructure <- Application <- Api`; only `Domain` referencing nothing is a hard rule.
- Return expected failures as `Result<T>` and let the mapping answer with a problem document; never hand-write an error body.
- Send requests through the mediator and register handlers by hand in `AddApplication`. Permission runs before validation, so an unauthorised caller never learns the shape of a request it may not send.
- Declare permissions on the request with `[RequiresPermission]` and keep `RequirePermission` on the route; the route gate rejects before the body is read, the behavior defends inside.
- The caching behavior prefixes every cache key with the tenant id; a query never supplies that prefix.
- The unit of work behavior saves each dirty context after a successful command, control plane first. Two databases are two units of work, so order them to fail closed.
- Handle outbox messages through `IOutboxMessageHandler`, matched by message type; the port lives in `Infrastructure` and the handlers in `Application`.
- Use EF Core by default; introduce Dapper for justified query needs.
- Avoid generic repositories and speculative abstractions.
- Store tenant business data, users, roles, and permissions in a separate PostgreSQL database for each tenant. Keep shared tenant, membership, and platform admin records in the `control` schema of `control_plane`.
- Treat tenant aliases as mutable path identifiers. Use each tenant's immutable GUID in `N` format in its database name, `tenant_<guid-N>`; never derive a database name from an alias.
- Reserve system and platform aliases so tenants cannot claim protected routes or identities.
- Use Clerk only for authentication; manage authorization in the backend. Keep platform privileges separate from tenant roles.
- Create database roles with the bootstrap script, never in EF migrations; the API runtime role never holds DDL privileges.
- Keep `src/Api/appsettings.Development.example.json` in step with every configuration key the API requires. The real `appsettings.Development.json` is gitignored, so the example is the only record of the expected shape; adding or renaming a connection string means updating it in the same change.
- Run migrations as a deploy step through `src/Migrator`, never at application startup. The migrator updates existing tenant databases and fails when one is missing; creating tenant databases belongs to provisioning.
- Resolve tenants and check membership through the read-only control plane credential. Tenant-surface endpoints that write control plane rows must scope every row to the resolved tenant, never to an identifier taken from the request.
- Require an `email` claim on the access token for invitation acceptance; possession of an invitation token alone must not grant entry. The Clerk JWT template must include `email`.
- Store only the hash of an invitation token; return the plaintext once, at creation.
- Tenant-scoped endpoints use `/{tenant-alias}/api/v{version}/...`.
- Resolve tenants from trusted control-plane data; fail closed on missing context.
- A membership record only grants entry to a tenant; remove it to revoke access. Check the tenant-database user status after membership and reject disabled users before permission checks.
- Enforce permissions before executing tenant operations.
- Persist business changes and outbox messages atomically. Delivery may repeat; make outbox processing idempotent.
- Queue provisioning work through the control plane outbox in the same transaction as the business change; every provisioning step must be safe to run again.
- Write a failing behavior test before implementing business logic or fixing bugs.
- Use xUnit with explicit `// Arrange`, `// Act`, and `// Assert` sections.
- Test persistence and tenant isolation against real PostgreSQL with Testcontainers.
- Keep tenant isolation and credential boundary tests in the `TenantIsolationTests` project.
- Keep API contracts versioned and generate shared client types from OpenAPI.
- Do not add comments that repeat the code; prefer clear names. Only explain non-obvious constraints or rationale, apart from required AAA labels in tests.
- Never log a request object: it can carry an invitation token or an email address.
- Run affected checks and report results.

## Commands

Run from `apps/api`:

- Create local settings (first checkout): `cp src/Api/appsettings.Development.example.json src/Api/appsettings.Development.json`
- Start PostgreSQL: `docker compose up -d`
- Stop PostgreSQL: `docker compose down`
- Reset PostgreSQL (drops all local data): `docker compose down -v && docker compose up -d`
- Create database roles (once per environment, superuser): `docker exec -i st-postgres psql -U postgres -v migrator_password=dev_migrator -v provisioner_password=dev_provisioner -v control_password=dev_control -v tenant_password=dev_tenant -f - < scripts/bootstrap-roles.sql`
- Grant control plane access (after the control plane migration): `docker exec -i st-postgres psql -U postgres -d control_plane -f - < scripts/grant-control-plane.sql`
- Build: `dotnet build Api.slnx`
- Test: `dotnet test --solution Api.slnx`
- Start API: `dotnet run --project src/Api --urls http://localhost:5000`
- Restore EF tool: `dotnet tool restore`
- Add control plane migration: `dotnet tool run dotnet-ef migrations add <Name> --project src/Infrastructure --startup-project src/Api --context ControlPlaneDbContext --output-dir Persistence/ControlPlane/Migrations`
- Add tenant migration: `dotnet tool run dotnet-ef migrations add <Name> --project src/Infrastructure --startup-project src/Api --context TenantDbContext --output-dir Persistence/Tenants/Migrations`
- Migrate control plane: `dotnet run --project src/Migrator -- migrate control-plane`
- Migrate existing tenants: `dotnet run --project src/Migrator -- migrate tenants`

- `/health` checks application liveness only.
- `/health/ready` checks PostgreSQL readiness and requires `ConnectionStrings__ControlPlane`.
