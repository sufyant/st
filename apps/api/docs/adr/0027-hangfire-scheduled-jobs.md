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

### How it works

- **Storage.** `Hangfire.PostgreSql` keeps Hangfire's tables in the `hangfire` schema, over the application's pooled data source (0019).
  - The migration step creates or updates the schema as the owner and grants the application role the use of its tables and sequences, like the message storage (0020).
  - The application starts with `PrepareSchemaIfNecessary = false` and never changes the schema.
  - Without `ConnectionStrings:Pooled` the host runs no jobs, so the build's OpenAPI step needs no database (0036).
  - The storage is the host's own service, not Hangfire's static `JobStorage.Current`, so hosts in one process (as in tests) never share it.
- **Server.** Every pod runs a Hangfire server with two workers. A job only sends a command or runs one statement, so two are enough, and one slow job does not hold up the other.
- **Jobs are commands.** A recurring job lives in its module's `X.Api` project and only sends a command through Wolverine (`IMessageBus.InvokeAsync`). The work therefore passes the host's pipeline like any other: logging, duration, validation and the transaction policy (0022). Only the host and the `X.Api` projects reference Hangfire.
- **Scheduling.** Each module exposes its system-defined jobs (`XModule.ScheduleJobs`); a hosted service schedules them on every start, after Wolverine has checked the database (0038). Scheduling an existing job only updates it.
- **The system jobs so far:**

  | Job | Schedule | Command |
  | --- | --- | --- |
  | `controlplane.close-expired-invitations` | Hourly | `CloseExpiredInvitations` (0029) |
  | `notifications.scan-due-notifications` | Every minute, never two at once | `ScanDueNotifications` (0037) |

- **User-defined notifications** are rows of `notifications.scheduled_notifications`, under row level security: a user schedules one for themselves, can change or cancel it until it is sent, and receives it once (0037). For now a notification is sent once at its time; recurring rules are added when a need appears.
- **The dashboard** is mapped at `/hangfire` in Development only, where Hangfire lets only local requests in. Elsewhere the path does not exist.

## Alternatives considered

- **Wolverine scheduled messages only.** One scheduler instead of two, but no dashboard and no natural home for user-editable schedules.
- **Quartz from the start.** Richer calendars than the template needs today.
- **Dashboard in production behind the API's authorization now.** The dashboard is a browser page and cannot send the bearer token that the API's token-centric authentication expects (0028). Solving that belongs to the system admin console.

## Consequences

- Accepted tension: two schedulers (Wolverine and Hangfire). No book supports this and it leans against simplicity; it was chosen for the dashboard and for user-defined jobs.
- The scanner reaches across tenants only as described in 0017.
- In production, jobs are observed through logs and metrics (0039) until the console exists.

## Verified

- Phase 6: `Hangfire.PostgreSql` 1.21.1 targets .NET Standard 2.0 and runs on .NET 10 with Npgsql 10 and `Hangfire.Core` 1.8.25. Both are LGPL-3.0, an open-source licence (0002).
- Phase 6: Hangfire's locks do not need the direct connection; they are rows in `hangfire.lock`, and Hangfire keeps no session state (0019).
