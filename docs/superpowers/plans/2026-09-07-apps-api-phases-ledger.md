# apps/api build — phase ledger

Autonomous overnight run, authorized by user 2026-09-07 to proceed through
all 6 phases without interactive per-phase approval. Each phase still gets
its own design spec + implementation plan (written by the controller from
the 29 ADRs in `docs/adr/`, no interactive Q&A) before execution via
superpowers:subagent-driven-development. Environment verified before start:
.NET SDK 10.0.102 present, Docker daemon running.

## Phases

1. Bootstrap — .NET 10 solution structure, monorepo wrapper (ADR 0028, 0029)
2. Domain + Persistence foundation — split into two sub-phases (this phase
   alone bundled too much for one SDD plan, same reasoning as the ADR
   suite's grouping):
   - **2a. Domain building blocks** — Entity/AggregateRoot/ValueObject/
     DomainEvent, Email/Money/TenantSlug value objects (ADR 0007, 0008).
     Pure domain layer, no EF, no DB, no Testcontainers needed yet.
   - **2b. Persistence foundation** — EF Core DbContext, admin schema
     (Tenants/Users/Memberships/Invitations/RolePermissions per ADR 0002
     as amended), schema-per-tenant runtime `search_path` (ADR 0001),
     migrations (ADR 0014), dual ID sequence generation (ADR 0008),
     RowVersion optimistic concurrency example (ADR 0015), UTC timestamp
     convention (ADR 0016). First phase needing Testcontainers + real
     Postgres (ADR 0025) — this is the security-critical layer.
3. Auth + Tenant middleware — JWT/JWKS, path-based resolution, membership,
   permission system (ADR 0003, 0004, 0005, 0006, 0013)
4. CQRS-lite + pipeline — hand-rolled mediator, pipeline behaviors, Result
   pattern, outbox (ADR 0009, 0010, 0011, 0017, 0018, 0019, 0020)
5. API surface — REST/v1, OpenAPI -> TS client, SignalR (ADR 0021, 0022,
   0023, 0024)
6. Test + observability infra — xUnit + Testcontainers, TenantIsolationTests,
   Serilog + Seq (ADR 0025, 0026, 0027)

## Status

- Phase 1: COMPLETE (commits 8143c9a..89e9d61 — solution skeleton, /health
  endpoint, pnpm/Turborepo wiring incl. turbo cache:false override for
  api#build/api#test found necessary during final review; pushed to origin)
- Phase 2a: COMPLETE (commits 4be50f2..a3bfe16 — Entity, AggregateRoot,
  ValueObject, DomainEvent base classes plus Email, Money, TenantSlug
  value objects; final-review fixes for reserved tenant slugs, non-generic
  IHasDomainEvents, and validation edge cases)
- Phase 2b: COMPLETE (commits 76e6772..bbd6972 — AdminDbContext, admin
  schema entities + migrations incl. FK constraints, TenantDbContext,
  tenant provisioning, dual-ID sequence generator, per-tenant migration
  runner, xmin optimistic concurrency on Membership, UTC audit
  interceptor, SafePostgresIdentifier shared validation; 86 tests incl.
  Testcontainers-backed integration suite; final review found and fixed
  a Critical tenant-schema-name-collision bug (TenantSlug max length
  63->56) plus 5 Important findings; pushed to origin)
- Phase 3: not started
- Phase 4: not started
- Phase 5: not started
- Phase 6: not started

## Notes

- Each phase's spec: `docs/superpowers/specs/2026-09-07-apps-api-phaseN-*.md`
- Each phase's plan: `docs/superpowers/plans/2026-09-07-apps-api-phaseN-*.md`
- Each phase's SDD workspace: `.superpowers/sdd/2026-09-07-apps-api-phaseN-*`
  (deleted on that phase's clean final review, per subagent-driven-development)
- If a phase's final review surfaces a finding that invalidates an earlier
  phase's foundation, STOP and report to the human partner rather than
  patching around it silently — this is exactly the scenario the "ask
  which governs" rule exists for, sleeping human partner or not.
