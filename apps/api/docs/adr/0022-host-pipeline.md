# 0022. Cross-cutting concerns live in the host pipeline

- Status: Proposed
- Date: 2026-10-04

## Context

Logging, validation, transactions and timing written in every handler are repeated, inconsistent and easy to forget. Modules should not depend on the libraries that implement them.

## Decision

- The host owns the shared pipeline: authentication, tenant resolution, error handling, logging, validation, transactions, performance measurement and OpenAPI. Each is written once and every command passes through it.
- Validation uses FluentValidation in the pipeline.
- Performance measurement records a duration histogram per command and logs a warning when a command exceeds a threshold from configuration (`Pipeline:SlowCommandThreshold`). The histogram is Wolverine's own `wolverine-execution-time`, recorded for every handled message including `InvokeAsync`, tagged with `message.type`. The warning is a Wolverine middleware in the host.
- Logging of commands uses Wolverine's built-in message logging; HTTP requests are logged once each by Serilog's request logging, apart from health checks, whose frequent polling would drown the log.
- Tenant resolution (0015) is HTTP middleware in the host. The transaction step is a Wolverine handler policy in the host, `TenantTransactionPolicy`: it opens one transaction for every message that carries a tenant and commits only when the handler succeeds (0016).
- Handlers contain no try/catch (0032).
- SharedKernel therefore does not depend on Wolverine, EF Core or FluentValidation.

## Alternatives considered

- **Per-module pipelines.** Duplication and drift between modules.
- **Pipeline behaviours in SharedKernel.** Pulls third-party dependencies into every module.

## Consequences

- Handlers stay focused on behaviour.
- The host is the one place where a cross-cutting change is made.
