You are building the initial skeleton of a multi-tenant SaaS starter template. The API lives in `apps/api/` of this monorepo; all paths below are relative to the repository root.

Before anything else, read `apps/api/AGENTS.md` and `apps/api/docs/ARCHITECTURE.md` in full. They are your instructions for the API. The `AGENTS.md` at the repository root holds general working guidelines and also applies. `ARCHITECTURE.md` is written in Turkish; ADRs and code are written in English.

Work in the phases below. At the end of every phase: run the full build and test suite, then stop and report what you built, which tests prove it, any decision you had to make that the architecture document does not cover, and anything you are unsure about. Do not start the next phase until I approve.

Throughout: work test-first, keep each phase in scope, and never weaken a boundary, isolation or licensing rule to make something pass. If the architecture document is ambiguous or contradicts itself, ask instead of guessing. The "Kurulumda doğrulanacaklar" list at the end of the document names things you must verify in the phase they come up, then record in the relevant ADR.

## Phase 0: Decisions and cleanup

- Remove the empty `docs/` folder at the repository root; ADRs live under `apps/api/docs/adr/`. Remove the dead `api#build` (`openapi/Api.json` output) and `@st/api-client#build` tasks from `turbo.json`.
- Split `apps/api/docs/ARCHITECTURE.md` into individual ADRs under `apps/api/docs/adr/` (one decision per record: context, decision, alternatives considered, consequences), status Proposed. Overwrite any ADRs from an earlier attempt.
- List any open questions that remain. Most were decided; do not reopen a decided point without a concrete reason.
- No code in this phase.

## Phase 1: Solution skeleton and guard rails

- `apps/api/Api.slnx`, module projects (empty) for ControlPlane, Notifications and Audit, SharedKernel (no third-party dependencies), the Api host and `Architecture.Tests` as the only test project; every other test project is created when its first test is written. Keep the root `build` script working.
- Repository-wide build settings: central package management, nullable enabled, warnings as errors, consistent analyzers.
- Architecture tests that encode the module and layer reference table. Write them first and show they fail on a deliberate violation before removing it.

## Phase 2: Cross-cutting pipeline

- Result and error types in SharedKernel; global exception handling to Problem Details in the host.
- Wolverine as mediator with middleware in the host for logging, validation, transactions and duration measurement. Verify whether Wolverine works with `internal` handlers and messages.
- OpenTelemetry (OTLP export configured from the environment, console locally) and Serilog, health checks, graceful shutdown.
- OpenAPI with Scalar and a committed OpenAPI document, URL segment versioning (`/v1/...`), per-tenant in-memory rate limiting from configuration (tenant resolution can be stubbed until Phase 3).

## Phase 3: Multi-tenancy core

- Database bootstrap script for the owner, application and read-only reporting roles; migrations run as a separate step after it, never on application start. Pooled and direct connection strings.
- ControlPlane module with the `catalog` schema: tenants (slug, status), users, memberships.
- Tenant resolution middleware: slug from `/v1/{tenant-slug}/...`, resolved to the tenant id, membership verified, tenant set on the transaction. One transaction per tenant-scoped request.
- `ITenantEntity` automation: shadow tenant column with a database default from the tenant setting, global query filter, RLS policies with `USING` and `WITH CHECK` generated into migrations.
- Tenant context for background work: message envelopes carry the tenant id and handlers set it; a narrow `SECURITY DEFINER` lookup for cross-tenant scanners. No role gets `BYPASSRLS`.
- Integration tests against real PostgreSQL (the Neon version) proving one tenant cannot read or write another tenant's data, applied automatically to every tenant entity, plus tests for the tenant-scoped catalog access point.

## Phase 4: Identity, invitations and authorization

- Clerk token validation (authentication only, Restricted sign-up mode, no Clerk Organizations).
- Permission catalogue in code, built-in roles with fixed permissions, tenant custom roles, membership based role assignment, permission based authorization policies. Enforce that a tenant always keeps at least one owner.
- Invitations in `catalog`: hashed single-use token, expiry, status. Clerk invitation through the backend API when the person has no Clerk account. The catalog user and the membership are created in one transaction when an invitation is accepted. Verify whether Clerk or our own Resend channel sends the invitation email.
- System admins in `catalog` (`system_admins` table, separate system permission pool), a seed script for the first system admin, an admin route group with its own authorization policy, tenant context entry for system admins that is always audited, and use of the read-only reporting role for cross-tenant reports.
- Tests for object, function and property level authorization, including that tenant custom roles can never hold system permissions, that system admins still go through RLS inside a tenant, and that invitation tokens cannot be reused or used after expiry.

## Phase 5: Messaging and the first end-to-end flow

- Durable outbox and inbox in a shared `wolverine` schema, integration events through module Contracts.
- Tenant onboarding saga in ControlPlane, started by a system admin: tenant created as `provisioning`, invitation sent to the first owner, tenant `active`; compensation marks it `failed`.
- `Onboarding.EndToEndTests` driving the whole flow through HTTP against the running host: system admin creates a tenant, the owner accepts the invitation and can act inside the tenant. This is the tracer bullet; it must pass before the phase ends.

## Phase 6: Remaining template modules

- Notifications with a channel abstraction: SignalR (Redis backplane off by default, enabled by configuration), Resend email adapter, fake email and push channels. User defined scheduled notifications with a Hangfire scanner job, system recurring jobs (including closing expired invitations). Hangfire in its own schema, dashboard behind the system admin permission.
- Audit module populated through outbox events: successful state-changing commands, denied authorization attempts and system admin tenant entry.
- Resilience policies on outbound HTTP clients.

When all phases are done, summarise the final structure, list every Proposed ADR, and list the open questions that remain.