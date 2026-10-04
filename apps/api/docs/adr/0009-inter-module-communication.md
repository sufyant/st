# 0009. Events between modules by default, synchronous contracts when needed

- Status: Proposed
- Date: 2026-10-04

## Context

Modules must cooperate without knowing each other's internals. Richardson (*Microservices Patterns*) and Kleppmann (*Designing Data-Intensive Applications*) favour asynchronous events for loose coupling, with synchronous calls only where an immediate answer is required.

## Decision

- **Event (default).** The publishing module publishes an integration event defined in its `*.Contracts` (for example `TenantCreatedEvent`) through the outbox (0024). It does not know its subscribers; adding a subscriber does not touch the publisher.
- **Synchronous call (only when the caller needs an answer now).** An interface in `*.Contracts` (for example `IControlPlaneModule`), implemented in the owning module's Infrastructure and wired through DI. It is an in-memory method call, never HTTP.
- A module never accesses another module's internal code.

## Alternatives considered

- **Direct calls into another module's Application.** Fast to write; dissolves the boundary.
- **HTTP between modules inside the host.** Network cost and failure modes without any benefit in one process.

## Consequences

- Most cross-module consistency is eventual.
- Synchronous contracts create runtime coupling and should stay few.
