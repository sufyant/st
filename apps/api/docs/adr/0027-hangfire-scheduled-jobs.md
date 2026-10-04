# 0027. Hangfire for scheduled jobs

- Status: Proposed
- Date: 2026-10-04

## Context

The template needs system-defined recurring jobs and user-defined scheduled work that runs exactly once across many pods, plus an operational dashboard.

## Decision

| Kind | Example | Lives in | Triggered by |
| --- | --- | --- | --- |
| System-defined | Nightly cleanup, closing expired invitations | Code | Hangfire recurring job |
| User-defined | "Remind me in 3 days", "send on the 1st of every month" | The owning module's tenant table (under RLS), cancellable and editable | A single Hangfire scanner job that runs every minute and processes due items |

- Both kinds are triggered on the server. Across pods, Hangfire's distributed lock makes each job run once.
- Hangfire uses its own `hangfire` schema.
- For now the dashboard is open only in local development and closed in production. Trigger: when the system admin console is built, the dashboard is opened from there behind a system admin permission.
- If complex calendar rules (business days, holidays) are really needed, Quartz is added for that job only.

## Alternatives considered

- **Wolverine scheduled messages only.** One scheduler instead of two, but no dashboard and no natural home for user-editable schedules.
- **Quartz from the start.** Richer calendars than the template needs today.
- **Dashboard in production behind the API's authorization now.** The dashboard is a browser page and cannot send the bearer token that the API's token-centric authentication expects (0028). Solving that belongs to the system admin console.

## Consequences

- Accepted tension: two schedulers (Wolverine and Hangfire). No book supports this and it leans against simplicity; it was chosen for the dashboard and for user-defined jobs.
- The scanner reaches across tenants only as described in 0017.
- In production, jobs are observed through logs and metrics (0039) until the console exists.

## To verify

- Licence and .NET 10 compatibility of the Hangfire PostgreSQL storage package (Phase 6).
- Whether Hangfire's locks need the direct connection (Phase 6, also 0019).
