# 0038. Stateless pods and horizontal scaling

- Status: Proposed
- Date: 2026-10-04

## Context

The application runs on Kubernetes with many pods. The Twelve-Factor App and Nygard's *Release It!* describe the properties this requires.

## Decision

| Concern | Decision |
| --- | --- |
| Double processing | No job runs twice across pods: Hangfire distributed lock; Wolverine row locks and inbox deduplication; idempotency in the business sense |
| State | Pods are stateless: files in object storage, session in the token, cache in Redis |
| Connections | Pooled and direct connections (0019); pod count does not exceed PostgreSQL's limit |
| Migrations | Separate one-off step (0020) |
| Shutdown | Health checks and graceful shutdown; a pod finishes its in-flight work before it stops |
| Real time | SignalR Redis backplane, enabled by configuration (0037) |
| Configuration | Read from the environment; no secrets in the repository |

Health checks are `/health/live`, which runs no checks and answers while the process can serve, and `/health/ready`, which runs the checks tagged `ready` (dependencies such as the database). The shutdown timeout is the generic host's `shutdownTimeoutSeconds` setting (for example `DOTNET_SHUTDOWNTIMEOUTSECONDS`), 30 seconds by default; it must stay below the orchestrator's termination grace period.

## Alternatives considered

- **Sticky sessions or in-pod state.** Breaks on scale-out and on pod restarts.

## Consequences

- Any handler may run on several pods and any message may arrive twice; handlers are idempotent.
