# 0018. Database roles and bootstrap script

- Status: Proposed
- Date: 2026-10-04

## Context

PostgreSQL table owners are not subject to RLS by default, so an application connecting as the owner would silently bypass isolation. Roles are cluster-level objects, while migrations are per database.

## Decision

| Role | Name | Purpose |
| --- | --- | --- |
| Owner | `api_owner` | Runs migrations; owns the tables |
| Application | `api_application` | Owns no table and has no `BYPASSRLS`; RLS always applies |
| Reporting (read-only) | `api_reporting` | Used only by system admin report endpoints; reads the catalog only |

### Bootstrap script

- The roles are created by `apps/api/db/bootstrap.sql`, which runs before migrations (0020). It runs as a role allowed to create roles: a superuser locally, a member of `neon_superuser` on Neon. Passwords are psql variables (`-v owner_password=...` and so on), never part of the repository.
- The script can be run again. It creates missing roles, and sets on every run: `LOGIN`, `NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`, and the given passwords. It grants the owner `CREATE` on the database, so the owner can create the module schemas, and revokes `CREATE` on the `public` schema from `PUBLIC`.

### Privileges come from migrations

- Each module's first migration grants the module's privileges on its schema. The catalog migration grants `USAGE` to the application and reporting roles. Through default privileges, the application role gets `SELECT, INSERT, UPDATE, DELETE` on the catalog's tables, and the reporting role `SELECT`.
- Migrations name the roles in plain text, so a migration never changes after it is written; `DatabaseRoles` (0048) holds the same names for code.
- The reporting role has read access to the `catalog` schema only and no access to tenant tables. Reports over tenant data are added by a separate ADR when a need appears.

### The application checks its role

The readiness check refuses a connection whose role is a superuser, has `BYPASSRLS` or owns a table (0014).

## Alternatives considered

- **One role for everything.** As table owner, the application would bypass RLS.
- **Create roles in migrations.** Roles are cluster-level and the migration role should not need to create them.
- **Reporting role reads tenant tables through an RLS policy for that role.** Would let one role see every tenant's data, without a present need.
- **Role names from configuration.** Migrations would no longer be fixed text, and every environment of the same template uses the same names anyway.

## Consequences

- Three credentials to configure, all from the environment (0038).
- Deployment runs the bootstrap script, then migrations (0020).
- No role needs `BYPASSRLS` or an all-tenant policy, consistent with 0017.
- A deployment that connects the application as the owner by mistake never becomes ready.
