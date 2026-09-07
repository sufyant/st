# apps/api build — phase ledger

Autonomous overnight run, authorized by user 2026-09-07 to proceed through
all 6 phases without interactive per-phase approval. Each phase still gets
its own design spec + implementation plan (written by the controller from
the 29 ADRs in `docs/adr/`, no interactive Q&A) before execution via
superpowers:subagent-driven-development. Environment verified before start:
.NET SDK 10.0.102 present, Docker daemon running.

## Phases

1. Bootstrap — .NET 10 solution structure, monorepo wrapper (ADR 0028, 0029)
2. Domain + Persistence foundation — Entity/AggregateRoot/ValueObject/DomainEvent,
   EF Core DbContext, schema-per-tenant, admin schema, migrations, dual ID
   (ADR 0001, 0002, 0007, 0008, 0012, 0014, 0015, 0016)
3. Auth + Tenant middleware — JWT/JWKS, path-based resolution, membership,
   permission system (ADR 0003, 0004, 0005, 0006, 0013)
4. CQRS-lite + pipeline — hand-rolled mediator, pipeline behaviors, Result
   pattern, outbox (ADR 0009, 0010, 0011, 0017, 0018, 0019, 0020)
5. API surface — REST/v1, OpenAPI -> TS client, SignalR (ADR 0021, 0022,
   0023, 0024)
6. Test + observability infra — xUnit + Testcontainers, TenantIsolationTests,
   Serilog + Seq (ADR 0025, 0026, 0027)

## Status

- Phase 1: not started
- Phase 2: not started
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
