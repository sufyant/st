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

## To verify

- Licence and .NET 10 compatibility of the maintained NetArchTest fork (Phase 1).
