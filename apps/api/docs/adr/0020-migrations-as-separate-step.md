# 0020. Migrations run as a separate step

- Status: Proposed
- Date: 2026-10-04

## Context

Several pods start at the same time, and the application role must not own tables (0018).

## Decision

- Migrations run as a separate, one-off step, never on application start.
- Order: the bootstrap script (0018), then the migrations.

## Alternatives considered

- **Migrate on application start.** Pods race each other and the application would need owner rights.

## Consequences

- Deployment needs a migration step before the new version starts (CI/CD itself is out of scope, 0045).
- Migrations must be compatible with the version still running during rollout.
