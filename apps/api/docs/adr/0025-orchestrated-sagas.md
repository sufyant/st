# 0025. Orchestrated sagas owned by the flow's module

- Status: Proposed
- Date: 2026-10-04

## Context

Multi-step flows need a visible owner and defined compensation when a step fails (Richardson, *Microservices Patterns*).

## Decision

- Sagas use orchestration, not choreography.
- A saga lives in the module that owns the flow.
- A saga's state lives in the owning module's own aggregates and schema. Each step persists it through the module's DbContext in the step's tenant transaction (0016), together with the step's work and the messages it sends (0024). No saga table is designed by hand.
- Every step has a defined compensation.

### How it works

- Each step is a durable message (0024), handled by an ordinary handler of the module; one handler class holds the flow's steps.
- A step is idempotent. The ids it creates are chosen by the saga and carried in its messages, and its state changes happen once.
- A step declares its failure policy next to its handler (0023): a few retries, then the dead letter queue. Wolverine then publishes the step's `Fault<T>` in the step's tenant (0024), and the saga's compensation handles it.
- Publishing a fault is best-effort. If the process dies between the dead letter move and the publish, the saga stays where it was, and the dead letter queue is the record of what failed.
- Wolverine's saga storage is not used. Each of its providers commits a unit of work of its own, so none of them can share the tenant transaction (Phase 5, WolverineFx 6.45.0):
  - Its lightweight storage opens a connection and a transaction of its own for the saga and keeps its table in the `wolverine` schema, not in the module's.
  - Its EF Core storage begins and commits the DbContext's transaction itself, whatever the handler returns, and its generated code needs the DbContext to be public (0007).

## Alternatives considered

- **Choreography.** No single place shows the flow or its failure handling.
- **Wolverine's lightweight saga storage.** The saga's state would commit apart from the step's work, in a shared schema.
- **Wolverine's EF Core saga storage.** Public DbContexts, and a second transaction model beside 0016.
- **Hand-made saga tables.** Duplicates state the module's aggregates already hold.

## Consequences

- The flow is readable in one place.
- A step's state, its work and the messages it sends commit together.
- A crash between a step's dead letter move and its fault leaves the saga unfinished until someone acts on the dead letter queue.
