# 0039. Observability with OpenTelemetry and Serilog

- Status: Proposed
- Date: 2026-10-04

## Context

Problems in a multi-tenant system are usually tenant-specific; signals without the tenant are hard to use.

## Decision

- OpenTelemetry for logs, metrics and traces, with Serilog for logging.
- Every signal carries the tenant id.
- Export through OTLP; the target comes from the environment. Locally, the console.
- Serilog writes logs to the console and forwards every event to the OpenTelemetry logger. Logs, metrics (ASP.NET Core and Wolverine) and traces (ASP.NET Core and Wolverine) are exported over OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, configured by the standard `OTEL_*` variables. Without it, logs go to the console and metrics and traces are not exported.
- Each host keeps its own Serilog logger and does not replace Serilog's static logger.
- The tenant id is added to logs, traces and metrics by tenant resolution (0015).
- Security events that cannot be written to the tenant-scoped audit log, such as authorization denials before a tenant is resolved (0040), are written to the log as security events.

## Alternatives considered

- **A vendor-specific agent.** Ties the template to a backend. The viewing tool is out of scope (0045).

## Consequences

- Any OTLP-compatible backend can be used.
