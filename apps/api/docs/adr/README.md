# Architecture Decision Records

See [0001](0001-record-architecture-decisions.md) for how records are written. The overview they were split from is [`../ARCHITECTURE.md`](../ARCHITECTURE.md).

| No. | Decision | Status |
| --- | --- | --- |
| 0001 | [Record architecture decisions](0001-record-architecture-decisions.md) | Proposed |
| 0002 | [No commercially licensed dependencies](0002-no-commercially-licensed-dependencies.md) | Proposed |
| 0003 | [Modular monolith on .NET 10 with a single Minimal API host](0003-modular-monolith-on-dotnet.md) | Proposed |
| 0004 | [Template modules](0004-template-modules.md) | Proposed |
| 0005 | [Module project structure](0005-module-project-structure.md) | Proposed |
| 0006 | [Module and layer reference rules enforced by tests](0006-module-reference-rules.md) | Proposed |
| 0007 | [Module types are internal by default](0007-internal-visibility-by-default.md) | Proposed |
| 0008 | [Each module owns its data](0008-module-data-ownership.md) | Proposed |
| 0009 | [Events between modules by default, synchronous contracts when needed](0009-inter-module-communication.md) | Proposed |
| 0010 | [Separate API, application and contract types](0010-separate-type-layers.md) | Proposed |
| 0011 | [PostgreSQL on Neon as a single database project](0011-postgresql-on-neon.md) | Proposed |
| 0012 | [EF Core code-first for data access](0012-ef-core-data-access.md) | Proposed |
| 0013 | [Shared database multi-tenancy with Row Level Security](0013-shared-database-multi-tenancy.md) | Proposed |
| 0014 | [Two-layer tenant isolation derived from ITenantEntity](0014-two-layer-tenant-isolation.md) | Proposed |
| 0015 | [Tenant resolution from the path slug and verified membership](0015-tenant-resolution.md) | Proposed |
| 0016 | [One transaction per tenant-scoped request and message](0016-tenant-scoped-transaction.md) | Proposed |
| 0017 | [Tenant context in background work](0017-tenant-context-in-background-work.md) | Proposed |
| 0018 | [Database roles and bootstrap script](0018-database-roles.md) | Proposed |
| 0019 | [Pooled and direct database connections](0019-pooled-and-direct-connections.md) | Proposed |
| 0020 | [Migrations run as a separate step](0020-migrations-as-separate-step.md) | Proposed |
| 0021 | [Catalog schema without RLS and a tenant-scoped access point](0021-catalog-schema.md) | Proposed |
| 0022 | [Cross-cutting concerns live in the host pipeline](0022-host-pipeline.md) | Proposed |
| 0023 | [Wolverine for mediator, messaging, outbox and sagas](0023-wolverine.md) | Proposed |
| 0024 | [Durable outbox and inbox](0024-durable-outbox-and-inbox.md) | Proposed |
| 0025 | [Orchestrated sagas owned by the flow's module](0025-orchestrated-sagas.md) | Proposed |
| 0026 | [Tenant onboarding saga](0026-tenant-onboarding-saga.md) | Proposed |
| 0027 | [Hangfire for scheduled jobs](0027-hangfire-scheduled-jobs.md) | Proposed |
| 0028 | [Clerk for authentication only, in Restricted mode](0028-clerk-authentication.md) | Proposed |
| 0029 | [Invite-only user creation](0029-invite-only-user-creation.md) | Proposed |
| 0030 | [Permission-based authorization with built-in and custom roles](0030-permission-based-authorization.md) | Proposed |
| 0031 | [System admins](0031-system-admins.md) | Proposed |
| 0032 | [Errors as Problem Details; expected failures as results](0032-errors-as-problem-details.md) | Proposed |
| 0033 | [URL segment API versioning from day one](0033-url-segment-versioning.md) | Proposed |
| 0034 | [Pagination by default](0034-pagination-by-default.md) | Proposed |
| 0035 | [Per-tenant in-memory rate limiting](0035-per-tenant-rate-limiting.md) | Proposed |
| 0036 | [OpenAPI as the single source, Scalar UI, committed document](0036-openapi-and-scalar.md) | Proposed |
| 0037 | [Notification channel abstraction](0037-notification-channels.md) | Proposed |
| 0038 | [Stateless pods and horizontal scaling](0038-stateless-horizontal-scaling.md) | Proposed |
| 0039 | [Observability with OpenTelemetry and Serilog](0039-observability.md) | Proposed |
| 0040 | [Audit log as business data through the outbox](0040-audit-log.md) | Proposed |
| 0041 | [Resilience on outbound calls](0041-outbound-resilience.md) | Proposed |
| 0042 | [Testing strategy](0042-testing-strategy.md) | Proposed |
| 0043 | [Test writing conventions](0043-test-writing-conventions.md) | Proposed |
| 0044 | [Test tooling](0044-test-tooling.md) | Proposed |
| 0045 | [Template scope and deferred topics](0045-template-scope.md) | Proposed |
| 0046 | [API build and tests run as Turborepo tasks](0046-api-build-through-turborepo.md) | Proposed |
| 0047 | [Types Wolverine discovers are public](0047-wolverine-visible-types-are-public.md) | Proposed |
