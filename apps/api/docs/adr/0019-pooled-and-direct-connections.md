# 0019. Pooled and direct database connections

- Status: Proposed
- Date: 2026-10-04

## Context

With many pods, direct connections would exceed PostgreSQL's connection limit. Pooled connections do not keep session state, which breaks session-level locks and `LISTEN/NOTIFY`.

## Decision

- Requests use the pooled connection.
- Migrations, and background components that need session-level locks or `LISTEN/NOTIFY`, use the direct connection.

## Alternatives considered

- **Direct connections only.** Pod count would exhaust the database's connection limit.
- **Pooled connections only.** Session-level features would fail.

## Consequences

- Two connection strings per environment.
- Each background component must be placed on the right connection (see To verify).

## To verify

- Whether Wolverine's durability agent needs the direct connection (Phase 5).
- Whether Hangfire's locks need the direct connection (Phase 6).
