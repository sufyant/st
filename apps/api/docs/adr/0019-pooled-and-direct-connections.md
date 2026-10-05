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
| `ConnectionStrings:Pooled` | Application | Pooler | Requests, message handlers and Hangfire (0027) |
| `ConnectionStrings:Direct` | Application | Direct | Wolverine's message storage and durability agent (0024) |
| `ConnectionStrings:Migrations` | Owner | Direct | The migration step only (0020) |
| `ConnectionStrings:Reporting` | Reporting | Pooler | System admin reports across tenants (0031) |

- **Settings added when first used.** The application role's direct connection came with the first background component that needs one, Wolverine's durability agent (Phase 5). The reporting role's connection came with the first report endpoint, the tenant list of the admin API (Phase 4). It is checked on first use, like `Pooled`.
- **Which work goes over which connection.** Wolverine's own work runs over `Direct`: leader election, recovery, node records, and marking messages handled. The messages a handler sends are written in its tenant transaction, over the pooled connection, so they commit with its work (0016). Hangfire runs over `Pooled`, on the application's pooled data source: it keeps no session state (see Verified).
- **The direct connection budget.** Each pod keeps at most `Maximum Pool Size` direct connections, set in `ConnectionStrings:Direct`; the recommended value is 5. Measured per pod (see Verified):

  | Component | Connection | Idle | Under load |
  | --- | --- | --- | --- |
  | Wolverine, on the leading pod | `Direct` | 3, one of which holds the leadership lock for as long as the pod leads | 5 without a cap; with a cap of 3, 1,000 messages were still all handled |
  | Wolverine, on any other pod | `Direct` | 2 | 4, the leading pod's count less the lock (not measured separately) |
  | Hangfire | `Pooled` | 0 direct | 0 direct |

  - So the direct budget is `pods × 5`, plus one owner connection while the migration step runs. It must stay below the database's connection limit, less the server connections the pooler itself opens.
  - `Pooled` counts against the pooler's client limit, not the database's; per pod it is capped by Npgsql's `Maximum Pool Size` (100 by default). Measured on an idle pod, Hangfire keeps about six of them open in the pool; under load requests, handlers and Hangfire together used twelve.
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
- Each pod keeps a few direct connections for Wolverine; while it leads, one holds the leadership lock. Their number is capped in the connection string (`Maximum Pool Size`, 5), so that pods times connections stays within the database's limit.
- A component added later is measured the same way and placed on the right connection before its numbers join the budget above.

## Verified

- Phase 5: Wolverine's durability agent needs the direct connection. Against WolverineFx 6.45.0, `pg_locks` and `pg_stat_activity` of a running host show the leader holding a session-level advisory lock (`pg_try_advisory_lock`) on a dedicated connection that is idle outside any transaction. Wolverine also renames that session with `set_config('application_name', ..., false)`. A transaction-mode pooler hands a server connection to other clients between transactions, so it can keep neither.
- Phase 6: Hangfire does not need the direct connection. Against Hangfire.PostgreSql 1.21.1, `pg_locks` and `pg_stat_activity` of a running host show no advisory lock and no `LISTEN` from Hangfire's sessions (long polling, which uses `LISTEN/NOTIFY`, stays off), and none of them idle inside a transaction. Its distributed locks are rows in its own `hangfire.lock` table, taken and released in short transactions.
- Phase 6: the budget above was measured on one host against PostgreSQL 18, with `Pooled` and `Direct` told apart by their `Application Name`, sampling `pg_stat_activity` every five seconds while idle and every 300 ms while 1,000 durable messages were handled. The host took the leadership after about 15 seconds; before that it held two direct connections. With `Maximum Pool Size=3` on `Direct` the same load completed: all 1,000 messages were handled, without errors.
