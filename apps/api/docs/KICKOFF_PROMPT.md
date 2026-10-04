You are building the initial skeleton of a multi-tenant SaaS starter template in this repository.

Before anything else, read `AGENTS.md` and `docs/ARCHITECTURE.md` in full. They are your instructions. `docs/ARCHITECTURE.md` is written in Turkish; ADRs and code are written in English.

Work in the phases below. At the end of every phase: run the full build and test suite, then stop and report what you built, which tests prove it, any decision you had to make that the architecture document does not cover, and anything you are unsure about. Do not start the next phase until I approve.

Throughout: work test-first, keep each phase in scope, and never weaken a boundary, isolation or licensing rule to make something pass. If the architecture document is ambiguous or contradicts itself, ask instead of guessing.

## Phase 0: Decisions

- Split `docs/ARCHITECTURE.md` into individual ADRs under `docs/adr/` (one decision per record: context, decision, alternatives considered, consequences), status Proposed.
- List every open question you find, including the ones in the document's open questions section.
- No code in this phase.

## Phase 1: Solution skeleton and guard rails

- Solution layout, module projects (empty), SharedKernel, the Api host and test projects as described in the document.
- Repository-wide build settings: central package management, nullable enabled, warnings as errors, consistent analyzers.
- Architecture tests that encode the module and layer reference rules. Write them first and show they fail on a deliberate violation before removing it.

## Phase 2: Cross-cutting pipeline

- Result and error types, global exception handling to Problem Details.
- Wolverine as mediator with middleware for logging, validation and transactions.
- OpenTelemetry and Serilog, health checks, graceful shutdown.
- OpenAPI with Scalar, API versioning, per-tenant rate limiting (tenant resolution can be stubbed until Phase 3).

## Phase 3: Multi-tenancy core

- ControlPlane module with the `catalog` schema: tenants, users, memberships.
- Tenant resolution middleware (path based) that verifies membership and sets the tenant on the database transaction.
- `ITenantEntity` automation: tenant column and global query filter in EF Core, RLS policies generated into migrations.
- Integration tests against real PostgreSQL proving one tenant cannot read or write another tenant's data, applied automatically to every tenant entity.
- Migrations run as a separate step, not on application start.

## Phase 4: Identity and authorization

- Clerk token validation (authentication only).
- Permission catalogue in code, built-in roles, tenant custom roles, membership based role assignment, permission based authorization policies.
- System admins (the SaaS provider's own staff) in the `catalog` schema (`system_admins` table, separate system permission pool), an admin route group with its own authorization policy, tenant context entry for system admins that is always audited, and a separate read-only database role for cross-tenant reporting that the normal application user never gets.
- Tests for object, function and property level authorization, including that tenant custom roles can never hold system permissions and that system admins still go through RLS when working inside a tenant.

## Phase 5: Messaging and the first end-to-end flow

- Durable outbox and inbox, integration events through module Contracts.
- Tenant onboarding saga in ControlPlane with compensation steps.
- `Onboarding.EndToEndTests` driving the whole flow through HTTP against the running host. This is the tracer bullet; it must pass before the phase ends.

## Phase 6: Remaining template modules

- Notifications with a channel abstraction (SignalR with Redis backplane, email, push), user defined scheduled notifications with a Hangfire scanner job, system recurring jobs.
- Audit module populated automatically through the pipeline.
- Resilience policies on outbound HTTP clients.
- Billing module skeleton demonstrating a tenant scoped module end to end.

When all phases are done, summarise the final structure, list every Proposed ADR, and list the open questions that remain.