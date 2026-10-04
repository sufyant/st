# 0025. Orchestrated sagas owned by the flow's module

- Status: Proposed
- Date: 2026-10-04

## Context

Multi-step flows need a visible owner and defined compensation when a step fails (Richardson, *Microservices Patterns*).

## Decision

- Sagas use orchestration, not choreography.
- A saga lives in the module that owns the flow.
- Wolverine persists saga state, in the owning module's schema; saga tables are not designed by hand.
- Every step has a defined compensation.

## Alternatives considered

- **Choreography.** No single place shows the flow or its failure handling.
- **Hand-made saga tables.** Duplicates what Wolverine provides.

## Consequences

- The flow is readable in one place.
- Saga storage follows Wolverine's model.
