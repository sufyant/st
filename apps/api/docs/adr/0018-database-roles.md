# 0018. Database roles and bootstrap script

- Status: Proposed
- Date: 2026-10-04

## Context

PostgreSQL table owners are not subject to RLS by default, so an application connecting as the owner would silently bypass isolation. Roles are cluster-level objects, while migrations are per database.

## Decision

| Role | Purpose |
| --- | --- |
| Owner | Runs migrations; owns the tables |
| Application | Owns no table and has no `BYPASSRLS`; RLS always applies |
| Reporting (read-only) | Used only by system admin report endpoints |

- The roles are created by a bootstrap script that runs before migrations.
- Privileges are granted by migrations.

## Alternatives considered

- **One role for everything.** As table owner, the application would bypass RLS.
- **Create roles in migrations.** Roles are cluster-level and the migration role should not need to create them.

## Consequences

- Three credentials to configure, all from the environment (0038).
- Deployment runs the bootstrap script, then migrations (0020).
