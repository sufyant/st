# 0019. Pooled and direct database connections

- Status: Proposed
- Date: 2026-10-04

## Context

With many pods, direct connections would exceed PostgreSQL's connection limit. Pooled connections do not keep session state, which breaks session-level locks and `LISTEN/NOTIFY`.

## Decision

- Requests use the pooled connection.
- Migrations, and background components that need session-level locks or `LISTEN/NOTIFY`, use the direct connection.
- Connection strings come from the environment:

| Setting | Role | Endpoint | Used by |
| --- | --- | --- | --- |
| `ConnectionStrings:Pooled` | Application | Pooler | Requests and message handlers |
| `ConnectionStrings:Direct` | Application | Direct | Wolverine's message storage and durability agent (0024) |
| `ConnectionStrings:Migrations` | Owner | Direct | The migration step only (0020) |
| `ConnectionStrings:Reporting` | Reporting | Pooler | System admin reports across tenants (0031) |

- **Settings added when first used.** The application role's direct connection came with the first background component that needs one, Wolverine's durability agent (Phase 5). The reporting role's connection came with the first report endpoint, the tenant list of the admin API (Phase 4). It is checked on first use, like `Pooled`.
- **Which work goes over which connection.** Wolverine's own work runs over `Direct`: leader election, recovery, node records, and marking messages handled. The messages a handler sends are written in its tenant transaction, over the pooled connection, so they commit with its work (0016).
- **Safety on the pooler.** The tenant setting is local to its transaction (0016), so it is gone before the pooler hands the connection to another client.
- **When `Pooled` and `Direct` are checked.** Neither is checked at start. The build starts the host to write the OpenAPI document (0036), and it has no database; in Phase 5 a check of `Direct` at start broke the build.
  - A missing `Pooled` setting is reported on first use.
  - Wolverine takes its data source while it is configured, so without `Direct` it runs without message storage.
  - In both cases the readiness check fails and the pod receives no traffic. The readiness check also refuses either connection whose role is a superuser, has `BYPASSRLS` or owns a table (0018).

## Alternatives considered

- **Direct connections only.** Pod count would exhaust the database's connection limit.
- **Pooled connections only.** Session-level features would fail.
- **Validate the connection strings on start.** Fails fast, but breaks the build's OpenAPI step.

## Consequences

- Four connection strings per environment today, more as background components arrive.
- Each background component must be placed on the right connection (see To verify).
- Each pod keeps a few direct connections for Wolverine; while it leads, one holds the leadership lock. Their number is capped in the connection string (`Maximum Pool Size`), so that pods times connections stays within the database's limit.

## To verify

- Whether Hangfire's locks need the direct connection (Phase 6).

## Verified

- Phase 5: Wolverine's durability agent needs the direct connection. Against WolverineFx 6.45.0, `pg_locks` and `pg_stat_activity` of a running host show the leader holding a session-level advisory lock (`pg_try_advisory_lock`) on a dedicated connection that is idle outside any transaction. Wolverine also renames that session with `set_config('application_name', ..., false)`. A transaction-mode pooler hands a server connection to other clients between transactions, so it can keep neither.
