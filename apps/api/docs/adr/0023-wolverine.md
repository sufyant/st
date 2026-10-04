# 0023. Wolverine for mediator, messaging, outbox and sagas

- Status: Proposed
- Date: 2026-10-04

## Context

The template needs in-process command handling, a transactional outbox and inbox, messaging between modules and sagas. MediatR and MassTransit v9+ are excluded by licence (0002). Innovation is accepted only where it adds value.

## Decision

- Wolverine (MIT) is the single tool for mediator, transactional outbox and inbox, messaging between modules and sagas.
- It is adopted layer by layer: commands and handlers first, then the outbox, then sagas.
- Handlers are named explicitly.
- Handler code is generated at runtime through `WolverineFx.RuntimeCompilation` (MIT). WolverineFx 6 no longer ships the runtime compiler in its core package; the alternative, pre-generated code (`TypeLoadMode.Static`), needs a regeneration step whenever a handler changes and can be adopted later if startup time requires it.
- The host includes each module's handler assemblies in Wolverine's discovery before it switches on the FluentValidation middleware, because validators are found only in the assemblies known at that moment.

## Alternatives considered

- **MediatR plus MassTransit.** Commercially licensed.
- **Hand-written mediator and outbox.** Rebuilds what a mature library provides.

## Consequences

- Accepted tension: Wolverine relies on conventions, and Ousterhout counts obscurity as a main source of complexity. Incremental adoption and explicit handler naming offset it.
- Handlers, their messages and validators must be public (0047).
- Runtime compilation adds Roslyn to the deployment and work to the first start.
