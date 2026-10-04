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
- Tenant isolation suite: every `ITenantEntity` automatically gets a "cannot read or write another tenant's data" test. The tenant-scoped catalog access point (0021) is tested the same way.
- Architecture tests and tenant isolation tests break the build.

## Alternatives considered

- **The classic test pyramid** (favoured by the company testing ADR). Suits logic-heavy code; for orchestration-heavy code it pushes tests onto mocks of our own database.
- **Test projects per layer.** Mirrors the structure instead of behaviour.

## Consequences

- Accepted tension: the company testing ADR favours the classic pyramid; the template chooses the integration-weighted (honeycomb) approach.
- Tests need Docker for Testcontainers.
