# 0048. A shared Tenancy project for tenant isolation plumbing

- Status: Proposed
- Date: 2026-10-04

## Context

Tenant isolation is derived from one marker, `ITenantEntity` (0014): a shadow tenant column, a query filter, a row level security policy in the migrations, and a transaction with the tenant set at its start (0016). All of this needs EF Core and Npgsql. Every module that holds tenant data needs the same code.

The reference table (0006) gives it no home: SharedKernel must stay free of third-party packages (0022), modules cannot reference the host, and the host's references are limited to the module `X.Api` projects.

## Decision

- A shared project, `src/Shared/Tenancy`, holds the plumbing. Of our projects it references only SharedKernel; of third-party packages, EF Core, Npgsql and EFCore.NamingConventions. It does not reference Wolverine; the Wolverine middleware that uses it lives in the host (0016).
- Only module `X.Infrastructure` projects and the host may reference it (0006).
- It contains:
  - `TenantContext`, the tenant of the current scope.
  - `TenantTransaction`, the scope's single connection and transaction (0016).
  - `TenantDbContext`, the base of a module DbContext that holds tenant entities, which applies the conventions of 0014.
  - The migrations SQL generator that writes the row level security policy (0014).
  - `TenantScan`, the SQL for narrow `SECURITY DEFINER` lookups (0017).
  - `DatabaseRoles`, the role names (0018).
  - `ITenantDirectory`, the abstraction tenant resolution uses (0015), implemented by ControlPlane. It returns a member's tenant with the permissions of their role (`TenantMembership`, 0030), and finds a tenant by slug for a system admin entering it (0031).
  - `ISystemAdminDirectory`, the system permissions of a user, for the admin routes (0031), implemented by ControlPlane.
  - `IModuleMigrator`, the migration step's view of a module database (0020).
- A module registers its DbContext with `AddModuleDbContext<TContext>(schema)`. This puts the context on the scope's connection, in its own schema, with snake_case names and the module's own migrations history table (0008). Each module has a design-time factory, so `dotnet ef` builds its model without starting the host.
- `SharedKernel` keeps only the `ITenantEntity` marker, so the domain stays free of persistence.

## Alternatives considered

- **Inject the configuration from the host.** The host would pass EF Core options, a model customizer and the SQL generator into each `AddXModule()`. No new project, but a module's model would depend on something configured elsewhere, which hides how isolation is applied (Ousterhout's obscurity).
- **Copy the plumbing into each module.** No change to the reference table, but the copies drift, and isolation is the place where drift is least acceptable.
- **Put it in SharedKernel.** Pulls EF Core and Npgsql into every module's Domain and Contracts.

## Consequences

- The reference table has one more row, enforced by the architecture tests like the others.
- A change in Tenancy changes isolation for every module at once; `Tenancy.IntegrationTests` proves its behaviour (0042).
- A module that adds an `ITenantEntity` to a context that does not derive from `TenantDbContext` gets no filter and no policy. The coverage check of 0042 fails the build when that happens.
- Tenancy also carries the lookups that decide who may enter a tenant, which go slightly beyond isolation plumbing. They live here because tenant resolution needs them, and Tenancy is the only shared project the host may reference other than SharedKernel, which takes no lookups of this kind.
