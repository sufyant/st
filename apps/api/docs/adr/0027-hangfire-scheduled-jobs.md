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
- Hangfire uses its own `hangfire` schema. Its dashboard is open only to system admins.
- If complex calendar rules (business days, holidays) are really needed, Quartz is added for that job only.

## Alternatives considered

- **Wolverine scheduled messages only.** One scheduler instead of two, but no dashboard and no natural home for user-editable schedules.
- **Quartz from the start.** Richer calendars than the template needs today.

## Consequences

- Accepted tension: two schedulers (Wolverine and Hangfire). No book supports this and it leans against simplicity; it was chosen for the dashboard and for user-defined jobs.
- The scanner reaches across tenants only as described in 0017.

## To verify

- Licence and .NET 10 compatibility of the Hangfire PostgreSQL storage package (Phase 6).
- Whether Hangfire's locks need the direct connection (Phase 6, also 0019).
