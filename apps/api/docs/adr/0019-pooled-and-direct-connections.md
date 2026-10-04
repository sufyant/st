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
| `ConnectionStrings:Migrations` | Owner | Direct | The migration step only (0020) |

- **Settings added when first used.** A direct connection for the application role comes with the first background component that needs one, Wolverine's durability agent (Phase 5). The reporting role's connection comes with the first report endpoint (Phase 4). Neither is configured before something uses it.
- **Safety on the pooler.** The tenant setting is local to its transaction (0016), so it is gone before the pooler hands the connection to another client.
- **When `Pooled` is checked.** A missing `Pooled` setting is reported on first use, not at start. The build starts the host to write the OpenAPI document (0036), and it has no database. Until the setting is there, the readiness check fails and the pod receives no traffic.

## Alternatives considered

- **Direct connections only.** Pod count would exhaust the database's connection limit.
- **Pooled connections only.** Session-level features would fail.
- **Validate the connection strings on start.** Fails fast, but breaks the build's OpenAPI step.

## Consequences

- Two connection strings per environment today, more as background components arrive.
- Each background component must be placed on the right connection (see To verify).

## To verify

- Whether Wolverine's durability agent needs the direct connection (Phase 5).
- Whether Hangfire's locks need the direct connection (Phase 6).
