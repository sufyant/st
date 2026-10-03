# AGENTS.md

This repository is a multi-tenant SaaS starter template: a .NET modular monolith API on PostgreSQL. Every future product is built on it, so correctness, clear boundaries and tenant isolation matter more than speed.

This file tells you how to think and work here. It does not tell you what to type. The decisions themselves live elsewhere.

## Sources of truth

1. `docs/adr/` holds the architecture decision records. They are authoritative.
2. `docs/ARCHITECTURE.md` is the overview the ADRs were split from.
3. This file holds working principles only. If it ever conflicts with an ADR, the ADR wins.

Read the relevant ADRs before changing anything in their area. If a task needs a decision no ADR covers, or contradicts one, stop and propose a new ADR instead of deciding silently. Do not copy ADR rules into code comments or other instruction files; reference the ADR.

## Non-negotiables

- **Tenant isolation is enforced by the database, not by trust.** Tenant data is protected by Postgres Row Level Security and an EF Core global filter, both derived from the `ITenantEntity` marker. Never bypass, disable or work around either. Never take the tenant from client input; it comes from the authenticated identity and verified membership.
- **Every tenant entity is covered by the isolation test suite.** If you add one, it must be covered automatically or the build is wrong.
- **Module boundaries are compiled and tested, not suggested.** A module references another module only through its `*.Contracts` project. Architecture tests enforce this; never weaken a rule to make code compile.
- **No commercially licensed dependencies** (MediatR, AutoMapper, MassTransit v9+, FluentAssertions and similar). Any new package needs a license check and, if it is architectural, an ADR.
- **No secrets in the repository.** Configuration comes from the environment.

## How we design

Reason from these principles. When unsure, ask what the listed sources would say.

- **Simplicity first.** Complexity is the main enemy. Prefer deep modules with small interfaces over many shallow layers. Do not build what the framework already gives you. Add capability when a real need appears, not in anticipation. (Ousterhout; Pragmatic Programmer)
- **The domain speaks the business language.** Names in the domain model come from the business, not from technical patterns. Aggregates protect their own invariants. (Evans; Vernon; Khononov)
- **Pure core, effects at the edges.** Business rules are side-effect free and easy to test. Persistence, messaging, time and I/O sit at the boundary. (functional programming; Khorikov)
- **Prefer events between modules.** Use an integration event through the outbox by default. Use a synchronous contract call only when the caller genuinely needs an answer now. Never reach into another module's internals or schema. (Richardson; Kleppmann)
- **Each module owns its data.** Its own schema, DbContext and migrations. No foreign keys across schemas; consistency across modules is handled in the application.
- **Expected failures are results, not exceptions.** Return a Result for business rule failures; let the global handler turn unexpected exceptions into a safe Problem Details response. No try/catch in handlers for control flow.
- **Check permissions, not roles.** Code asks "may this user do X in this tenant", never "is this user an admin".
- **Assume many instances.** Any handler may run on several pods and any message may arrive twice. Keep pods stateless and handlers idempotent. (Twelve-Factor; Release It!)
- **Every external call can fail.** Use timeouts, retries and circuit breakers at integration points. (Release It!)
- **Observability carries the tenant.** Logs, traces and metrics include the tenant id. Audit records are business data, written through the pipeline, not ad hoc.

## How we work

- **Test first.** Red, green, refactor. Start a feature from a failing test at the outer boundary and work inward. The refactor step is part of the work, not optional. (Beck; Freeman and Pryce)
- **Small, verifiable steps.** Each change should build, pass all tests and be reviewable on its own.
- **Stay in scope.** Do the task asked. Report adjacent problems; do not fix them uninvited.
- **Leave decisions visible.** If you made a choice a reviewer could reasonably question, say so in your summary.

## How we test

- Test behaviour, not implementation. A unit of test is a unit of behaviour, not a class. Refactoring must not break tests. (Khorikov; Ian Cooper)
- Domain logic gets fast, in-memory unit tests without mocks.
- Never mock our own database. Handlers and persistence are tested against real PostgreSQL, including RLS, using Testcontainers. Mock only systems we do not own.
- Verify an interaction only when the interaction itself is the requirement.
- Test code is production code. Arrange, Act, Assert; no branching or loops in tests; no expected values computed with production logic; deterministic time, ids and randomness; no sleeps. Readability beats DRY in tests. (Meszaros; Clean Code)
- A bug fix starts with a regression test shown to fail for the original defect.
- Fix flaky tests at the root; never add retries.
- Test projects are per module and per kind (`X.UnitTests`, `X.IntegrationTests`); cross-module flows live in `{Capability}.EndToEndTests`; system rules in `Architecture.Tests`. Architecture and tenant isolation tests must always pass.

## Recording decisions

New or changed architectural decisions are written as ADRs in `docs/adr/` (context, decision, alternatives considered, consequences). Propose them; do not mark them accepted yourself. (Harmel-Law)

## Definition of done

- Build passes with no new warnings.
- All tests pass, including architecture and tenant isolation tests.
- New behaviour is covered by tests written first.
- No boundary, isolation or licensing rule was bent.
- Any decision not covered by an ADR is raised, not buried.

## Reading list

The principles above are distilled from these. Use them to reason about cases this file does not cover.

| Area | Sources |
| --- | --- |
| Design and complexity | Ousterhout, *A Philosophy of Software Design*; Hunt and Thomas, *The Pragmatic Programmer*; Martin, *Clean Code* |
| Architecture | Richards and Ford, *Fundamentals of Software Architecture* (2nd ed.); Ford, Parsons, Kua, *Building Evolutionary Architectures*; Fowler, *Patterns of Enterprise Application Architecture*; Harmel-Law, *Facilitating Software Architecture* |
| Domain modelling | Evans, *Domain-Driven Design*; Vernon, *Implementing Domain-Driven Design*; Khononov, *Learning Domain-Driven Design* |
| Multi-tenancy | Golding, *Building Multi-Tenant SaaS Architectures*; Azure Architecture Center multitenant guidance |
| Data and messaging | Kleppmann and Riccomini, *Designing Data-Intensive Applications* (2nd ed.); Richardson, *Microservices Patterns* |
| Resilience and operations | Nygard, *Release It!*; The Twelve-Factor App |
| Security | OWASP API Security Top 10 |
| Testing | Beck, *Test-Driven Development by Example*; Freeman and Pryce, *Growing Object-Oriented Software, Guided by Tests*; Khorikov, *Unit Testing Principles, Practices, and Patterns*; Meszaros, *xUnit Test Patterns*; Feathers, *Working Effectively with Legacy Code* |
| C# | Skeet, *C# in Depth* |