# 0023. Wolverine for mediator, messaging, outbox and sagas

- Status: Proposed
- Date: 2026-10-04

## Context

The template needs in-process command handling, a transactional outbox and inbox, messaging between modules and sagas. MediatR and MassTransit v9+ are excluded by licence (0002). Innovation is accepted only where it adds value.

## Decision

- Wolverine (MIT) is the single tool for mediator, transactional outbox and inbox, messaging between modules and sagas.
- It is adopted layer by layer: commands and handlers first, then the outbox, then sagas.
- Handlers are named explicitly.

## Alternatives considered

- **MediatR plus MassTransit.** Commercially licensed.
- **Hand-written mediator and outbox.** Rebuilds what a mature library provides.

## Consequences

- Accepted tension: Wolverine relies on conventions, and Ousterhout counts obscurity as a main source of complexity. Incremental adoption and explicit handler naming offset it.
- Visibility constraints may follow from its code generation (0007).
