# 0044. Test tooling

- Status: Proposed
- Date: 2026-10-04

## Context

The testing strategy (0042) needs a test framework, assertions, real PostgreSQL in tests, in-memory hosting of the API and architecture rule checks, all under the licensing policy (0002).

## Decision

- xUnit as the test framework.
- Shouldly for assertions.
- Testcontainers for real PostgreSQL (the Neon version, 0011).
- WebApplicationFactory to host the API in tests.
- NetArchTest, the maintained fork, for architecture tests.

## Alternatives considered

- **FluentAssertions.** Commercially licensed (0002).
- **An in-memory database provider.** Does not run RLS or real SQL.

## Consequences

- No licensed test dependencies.
- xUnit v3 runs on Microsoft Testing Platform. The .NET 10 SDK no longer runs it through VSTest, so `apps/api/global.json` opts `dotnet test` into Microsoft Testing Platform and the solution is tested with `dotnet test --solution Api.slnx`.

## Verified

- The maintained NetArchTest fork is `NetArchTest.eNhancedEdition` (1.4.5): MIT licence, targets .NET Standard 2.0 and depends only on Mono.Cecil (MIT). It runs on .NET 10; the architecture tests were shown to fail on deliberate violations in Phase 1. Its latest release is from June 2025, so its maintenance pace is worth watching.
