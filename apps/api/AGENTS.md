# AGENTS.md

This repository is a multi-tenant SaaS starter template. Its core is a .NET modular monolith API on PostgreSQL in `apps/api/`. Every future product is built on it, so correctness, clear boundaries and tenant isolation matter more than speed.

This file tells you how to think and work here. It does not tell you what to type. The decisions themselves live in one place.

## Source of truth

1. `apps/api/docs/ARCHITECTURE.md` holds every architectural rule. It is authoritative. Each rule has an id (for example `R4`, `W2`).
2. This file holds working principles only. If it ever conflicts with `ARCHITECTURE.md`, `ARCHITECTURE.md` wins.

Read the relevant section of `ARCHITECTURE.md` before changing anything in its area. If a task needs a decision that the document does not cover, or contradicts one, stop and raise it. Do not decide silently. Do not copy rules into code comments or other instruction files; cite the rule id.

The older ADRs, the older architecture overview and the kickoff prompt were removed. Do not restore them and do not rely on a memory of them.

## Non-negotiables

- **Tenant isolation is enforced by the database, not by trust.** Tenant data is protected by PostgreSQL Row Level Security, enabled and forced, with an application account that is not the table owner. RLS is the single source of truth; do not add an EF Core query filter as a second layer. Never bypass, disable or work around RLS.
- **The tenant id comes from the API path and is proven by membership.** Never take it from the body, the query string or a header. The id in the path gives no access by itself; verified identity and membership do.
- **Every tenant table is covered by the isolation test suite.** If you add one, it must be covered automatically or the build is wrong.
- **Module boundaries are compiled and tested, not suggested.** A module references another module only through its `*.Contracts` project. An `Api` project never references an `Infrastructure` project. Architecture tests enforce this; never weaken a rule to make code compile.
- **Wolverine is used as it is.** It is the dispatcher, the pipeline, the outbox and the saga engine. Do not write a custom transaction wrapper, mediator, outbox or saga store. `Domain` and `Contracts` projects never reference Wolverine.
- **Stay inside the scope of the first template.** The scope and the deferred list are in `ARCHITECTURE.md`. Do not add a deferred feature or tool.
- **No commercially licensed dependencies** (MediatR, AutoMapper, MassTransit v9+, FluentAssertions and similar). Any new package needs a license check and, if it is architectural, a decision in `ARCHITECTURE.md`.
- **No secrets in the repository.** Configuration comes from the environment.

## How we design

Reason from these principles. When unsure, ask what the listed sources would say.

- **Simplicity first.** Complexity is the main enemy. Prefer deep modules with small interfaces over many shallow layers. Do not build what the framework or Wolverine already gives you. Add capability when a real need appears, not in anticipation. (Ousterhout; Pragmatic Programmer)
- **The domain speaks the business language.** Names in the domain model come from the business, not from technical patterns. Aggregates protect their own invariants. (Evans; Vernon; Khononov)
- **Pure core, effects at the edges.** Business rules are side-effect free and easy to test. Persistence, messaging, time and I/O sit at the boundary. A saga decides and returns messages; it does not call external services. (Khorikov)
- **Read through contracts, tell through events.** A module that needs data calls the other module's contract. A module that reports something publishes an integration event through the outbox. Never reach into another module's internals or schema. (Richardson; Kleppmann)
- **Each module owns its data.** Its own schema, DbContext and migrations. No foreign keys and no joins across schemas; consistency across modules is handled in the application.
- **Reject before you change.** A business rule rejection returns before any data changes. A failure after a change is an exception, because Wolverine commits a handler that returns normally (W7). Let the global handler turn unexpected exceptions into a safe Problem Details response.
- **Check permissions, not roles.** Code asks "may this user do X in this tenant", never "is this user an admin".
- **The host has no business rules.** It connects the modules. A rule about who may do what belongs to a module.
- **Assume many instances.** Any handler may run on several pods and any message may arrive twice. Keep pods stateless and handlers idempotent. (Twelve-Factor; Release It!)
- **Every external call can fail.** Each one has a time limit and a bounded retry. (Release It!)
- **Observability carries the trace.** The trace id is the same in the log, in the event and in the handler. Audit records are business data, written by the Audit module from events.

## How we work

- **Test first.** Red, green, refactor. Start a feature from a failing test at the outer boundary and work inward. A fix starts with a red test that checks the rule. The refactor step is part of the work, not optional. (Beck; Freeman and Pryce)
- **Small, verifiable steps.** Each change should build, pass all tests and be reviewable on its own.
- **Stay in scope.** Do the task asked. Report adjacent problems; do not fix them uninvited.
- **Leave decisions visible.** If you made a choice a reviewer could reasonably question, say so in your summary.

## How we test

- Test behaviour, not implementation. A unit of test is a unit of behaviour, not a class. Refactoring must not break tests. (Khorikov; Ian Cooper)
- Domain logic and sagas get fast, in-memory unit tests without mocks.
- Never fake our own database. Handlers and persistence are tested against real PostgreSQL, including RLS, using Testcontainers. Tests connect with the application account, not the migration account. Fake only systems we do not own, with a hand-written implementation of our own interface.
- Verify an interaction only when the interaction itself is the requirement.
- Test code is production code. Arrange, Act, Assert; no branching or loops in tests; no expected values computed with production logic; deterministic time, ids and randomness; no sleeps. Readability beats DRY in tests. (Meszaros; Clean Code)
- Test names follow `Operation_Scenario_ExpectedOutcome`, where `Operation` is the name of the work, not the method name. Each test project carries a `TestClassification` trait.
- Fix flaky tests at the root; never add retries.
- Test projects are per module and per kind (`X.UnitTests`, `X.IntegrationTests`) and sit next to their module. System-wide tests (architecture, end to end) stay in the top `tests` folder. Architecture and tenant isolation tests must always pass.

## Recording decisions

`ARCHITECTURE.md` is the record. A new or changed architectural decision is proposed as a change to that document, with its book source. If it deviates from the books, it goes into the deviations table with the reason. Propose it; do not accept it yourself.

## Definition of done

- Build passes with no new warnings.
- All tests pass, including architecture and tenant isolation tests.
- New behaviour is covered by tests written first.
- No boundary, isolation, scope or licensing rule was bent.
- Any decision not covered by `ARCHITECTURE.md` is raised, not buried.

## Reading list

The principles above are distilled from these. Use them to reason about cases this file does not cover.

| Area | Sources |
| --- | --- |
| Design and complexity | Ousterhout, *A Philosophy of Software Design*; Hunt and Thomas, *The Pragmatic Programmer*; Martin, *Clean Code* |
| Architecture | Richards and Ford, *Fundamentals of Software Architecture* (2nd ed.); Ford, Parsons, Kua, *Building Evolutionary Architectures*; Fowler, *Patterns of Enterprise Application Architecture* |
| Domain modelling | Evans, *Domain-Driven Design*; Vernon, *Implementing Domain-Driven Design*; Khononov, *Learning Domain-Driven Design* |
| Multi-tenancy | Golding, *Building Multi-Tenant SaaS Architectures*; Azure Architecture Center multitenant guidance |
| Data and messaging | Kleppmann and Riccomini, *Designing Data-Intensive Applications* (2nd ed.); Richardson, *Microservices Patterns*; Hohpe and Woolf, *Enterprise Integration Patterns* |
| Testing | Freeman and Pryce, *Growing Object-Oriented Software, Guided by Tests*; Khorikov, *Unit Testing Principles, Practices, and Patterns* |
| Resilience and operations | Nygard, *Release It!*; The Twelve-Factor App |
| Security | OWASP API Security Top 10; OWASP ASVS |
