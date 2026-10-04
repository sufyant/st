# 0042. Testing strategy

- Status: Proposed
- Date: 2026-10-04

## Context

The template is the base for every product; tests must allow refactoring without breaking and must prove isolation and boundaries. Most code orchestrates persistence and messaging rather than containing complex algorithms (Khorikov, *Unit Testing Principles, Practices, and Patterns*; Ian Cooper).

## Decision

- Test-first from day one: red, green, refactor. A feature starts from a failing acceptance test at the outer boundary and works inward (Beck; Freeman and Pryce). The refactor step is not skipped.
- Principles:
  1. Test behaviour, not implementation; a unit of test is a unit of behaviour, not a class.
  2. The domain stays pure; its tests are mock-free, many and fast.
  3. Our own database is never mocked. PostgreSQL, including RLS, is tested for real with Testcontainers. Only external systems (Clerk, email, push) are mocked.
  4. An interaction is verified only when it is itself the requirement ("the notification is sent exactly once").
  5. Test code has production quality.
- Test projects are per module and per kind. The list below is the target set; each project is created when its first test is written:

```text
apps/api/tests/
  ControlPlane.UnitTests/
  ControlPlane.IntegrationTests/
  Notifications.UnitTests/
  Notifications.IntegrationTests/
  Audit.IntegrationTests/
  SharedKernel.UnitTests/
  Api.IntegrationTests/           # host behaviour: Problem Details, versioning, rate limiting
  Architecture.Tests/             # cross-module rules, NetArchTest
  Onboarding.EndToEndTests/       # cross-module flow, named after the capability
```

- The weight is on module integration tests: handlers are tested against a real database.
- Host behaviour that needs endpoints of its own (result mapping, validation, rate limiting) is tested in `Api.IntegrationTests` on a test server that composes the host's real pipeline with test endpoints and handlers. Behaviour of the composed application uses WebApplicationFactory (0044).
- Tenant isolation suite, in three parts:
  - **Behaviour.** `Tenancy.IntegrationTests` proves the mechanism of 0014 on a fixture tenant entity against real PostgreSQL:
    - A tenant cannot read, update, delete or write another tenant's rows.
    - Row level security alone and the query filter alone each hide them.
    - Without a tenant, nothing is read and nothing written.
    - Uncommitted work is discarded.
    - The tenant setting ends with its transaction.
    - The `SECURITY DEFINER` lookup (0017) returns only `(tenant_id, id)`.
  - **Coverage.** `Api.IntegrationTests` finds every `ITenantEntity` in every module DbContext the host registers, with no list to keep up to date. It checks, after the real migrations, that each has its query filter, row level security, exactly one policy limiting reads and writes to the active tenant, and the tenant column default. A tenant entity added anywhere is covered without anyone writing a test, and one that lacks isolation fails the build.
  - **Catalog.** `ControlPlane.IntegrationTests` proves that the tenant-scoped catalog access point (0021) shows and stamps only the active tenant.
- Architecture tests and tenant isolation tests break the build.
- **Test databases.** Each test project that needs PostgreSQL starts one container for its assembly and sets it up the way a deployment is: the bootstrap script, then the migrations as the owner (0018, 0020). Tests connect as the application role. Tests share the database, so each works with tenants, users and slugs of its own rather than resetting data.
- **Pipeline tests.** The host's pipeline tests drive the tenant pipeline through stand-in handlers and a stand-in tenant entity of their own, so they do not depend on any module's features.
- **Signed-in users in tests.** Tests sign session tokens shaped like Clerk's with a key of their own. The host is configured to trust that key instead of fetching Clerk's, so the rest of token validation (issuer, lifetime, authorized party, second factor) is the production code (0028).
- **External systems are fakes at their port.** Clerk's Backend API and the invitation email are replaced by fakes of their ports (`IIdentityProvider`, `IInvitationSender`) in module and host tests. The real Clerk adapter is tested on its own against a stubbed HTTP handler that records its requests.
- **Module handlers without the host.** Module integration tests call handlers directly in a tenant transaction they open the way the host's transaction policy does, because module test projects cannot reference the host (0006).
- **Concurrency.** A test of a lock waits, through `pg_blocking_pids`, until the second transaction is blocked before the first commits, with a timeout instead of a sleep. Without the lock the wait times out and the test fails.

## Alternatives considered

- **The classic test pyramid** (favoured by the company testing ADR). Suits logic-heavy code; for orchestration-heavy code it pushes tests onto mocks of our own database.
- **Test projects per layer.** Mirrors the structure instead of behaviour.

## Consequences

- Accepted tension: the company testing ADR favours the classic pyramid; the template chooses the integration-weighted (honeycomb) approach.
- Tests need Docker for Testcontainers.
