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
- Phase 3: COMPLETE (commits 1aaccce..0af9f1d — JWT Bearer auth with
  locally-signed test tokens, path-based tenant resolution + membership
  enforcement, RolePermission-based endpoint authorization, all proven
  via /{tenant-alias}/api/v1/whoami; 100 tests incl. Testcontainers
  suite. Final review found and fixed a Critical claim-shadowing
  vulnerability (JWT-supplied claims could shadow DB-derived tenant/
  role claims) — took 2 fix rounds since round 1's regression test
  proof was itself too weak, closed properly in round 2 with
  independently-reproduced RED/GREEN evidence. Also: aligned permission
  checking with ADR 0006 (claims loaded at tenant-resolution time, not
  queried at authorization time), added the IMemoryCache tenant lookup
  ADR 0017 mandates, renamed the route param to match ADR 0004's
  {tenant-alias}. Architectural question (Api.Infrastructure now
  depends on ASP.NET Core) resolved: leave as-is, no move warranted.
  Non-blocking follow-up noted: a membership_role/role leak-symmetry
  test (mirroring the tenant_id one) could be added later. Pushed to
  origin.)
- Phase 4: COMPLETE (commits 37b2d3f..68259db — Result<T>, hand-rolled
  mediator (reflection-dispatched, dynamic fell back due to DLR
  accessibility rules), Logging/Validation/Permission pipeline
  behaviors, RenameTenantCommand (chosen over reusing Phase 2b's
  TenantProvisioningService, whose self-managed transaction would
  conflict with pipeline UoW ownership), ITenantRepository to keep EF
  Core out of Api.Application, outbox pattern with SELECT FOR UPDATE
  SKIP LOCKED; 124 tests. Final review found and fixed a Critical
  cross-tenant write vulnerability (RenameTenantCommand had no check
  that the caller's tenant_id claim matched the target tenant —
  closed with ITenantScopedRequest), a reproduced flaky test from an
  unscoped shared-DB query, OutboxProcessor's real code path never
  being exercised by any test, pipeline order contradicting ADR 0006
  (Permission must run before Validation), and OutboxProcessor as a
  global hosted service contaminating every host-booting test. Noted
  for Phase 5: PermissionBehavior (mediator) and
  PermissionAuthorizationHandler (Phase 3 HTTP policy) both check the
  same claim via different mechanisms — reconcile once real endpoints
  exist; RenameTenantCommandEndToEndTests' own test harness still has
  un-swapped behavior order, harmless today but align later. Pushed to
  origin.)
- Phase 5: COMPLETE (commits e7d25fb..de17143 — real `PUT
  /{tenant-alias}/api/v1/tenant` endpoint wired to Phase 4's
  RenameTenantCommand via ResultHttpMapper; OpenAPI generation
  (Microsoft.AspNetCore.OpenApi + Microsoft.Extensions.ApiDescription.Server,
  runtime doc at /openapi/v1.json, build-time file at
  apps/api/openapi/Api.Host.json — note: filename is Api.Host.json, not
  v1.json); packages/api-client scaffolded and generating a real TypeScript
  client via openapi-typescript + openapi-fetch (committed, not gitignored);
  SignalR TenantHub at /{tenant-alias}/hubs/tenant. 134 tests (86 unit + 48
  integration). Final review (opus) found 0 Critical, 5 Important: (1)
  OpenAPI doc omitted the tenant-alias path parameter, making the generated
  TS client unable to address any tenant-scoped route — fixed by binding
  [FromRoute(Name="tenant-alias")] on both handlers; (2) SignalR JWT auth
  had no query-string access_token support real browser clients need —
  fixed via JwtBearerEvents.OnMessageReceived, but round-1's fix collided
  with finding (3)'s route change and needed a round-2 correction; (3)
  /hubs/tenant bypassed TenantResolutionMiddleware entirely (no
  tenant-alias route segment) — fixed by moving the hub under
  /{tenant-alias}/hubs/tenant; (4) ResultHttpMapper's unknown-error 500
  branch had no logging and leaked internal Error.Message to clients —
  fixed with ILogger + a generic 500 detail message; (5)
  packages/api-client's build script raced apps/api's own dotnet build
  under Turborepo (no workspace dependency declared) — fixed with an
  "api": "workspace:*" dependency. Two fix rounds plus a post-hoc fix: an
  automated background security scan caught round-2's own fix for (2)
  using an unanchored Contains("/hubs/") substring match (a tenant slug of
  literally "hubs" would make /hubs/api/v1/whoami match too, leaking
  query-string tokens onto the REST surface) — corrected to an exact
  path-segment check. Parked Minor findings (not fixed, non-blocking):
  OpenAPI doc's PUT only documents 200 (actual: 204/400/403/404/500, no
  .Produces() annotations); no securitySchemes in the OpenAPI doc (three
  authenticated endpoints appear public in the schema); MapOpenApi() is
  unconditional rather than gated to Development; a pre-existing (Phase 3)
  tenant-slug-enumeration timing difference (404 vs 401) was extended to
  the new endpoint, not introduced by it. Also noted: the design spec's
  acceptance criterion 1 still says the old `/tenants/{tenantId}` route
  shape; the plan's Global Constraint (singular `/tenant`, no client-
  supplied id) is what was actually built and is the right call — the spec
  doc itself was never updated to match, cosmetic only. Pushed to origin.)
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
