# 0011. PostgreSQL on Neon as a single database project

- Status: Proposed
- Date: 2026-10-04

## Context

Tenant isolation is enforced by Row Level Security (0013, 0014), which PostgreSQL provides. The template needs a managed database that is cheap to start with.

## Decision

- The database is PostgreSQL on Neon, a single Neon project shared by all modules, each in its own schema (0008).
- The PostgreSQL major version is 18, the version of the Neon project. Tests run against PostgreSQL 18 as well.

## Alternatives considered

The overview records no alternative providers. Any other managed PostgreSQL would keep the rest of the architecture unchanged; Neon's pooled and direct endpoints shape 0019.

## Consequences

- One database to operate, back up and migrate.
- Test containers must be updated together with the Neon project's major version.
