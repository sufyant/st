# 0003. Modular monolith on .NET 10 with a single Minimal API host

- Status: Proposed
- Date: 2026-10-04

## Context

New products start small, with a small team and unknown boundaries, yet the template must keep those boundaries strict from day one. Mature, proven technology is preferred over fashion. Richards and Ford (*Fundamentals of Software Architecture*, 2nd ed.) recommend starting with a modular monolith and extracting services only when a real need appears.

## Decision

- The backend is a modular monolith on .NET 10: one deployable host built with Minimal API, with modules as class libraries inside it.
- Modules have strict boundaries (0005, 0006, 0008) so that one can be extracted into a separate service later if that is genuinely needed.

## Alternatives considered

- **Microservices from the start.** Distributed-system cost (network, deployment, consistency) before the boundaries are known.
- **A layered monolith without module boundaries.** Cheap to start, but boundaries erode and extraction becomes impossible.
- **MVC controllers.** More ceremony than Minimal API for the same result.

## Consequences

- One process to build, deploy and observe; calls between modules are in-memory.
- Boundaries are not given by the network, so they must be enforced by the build (0006).
- The whole host scales horizontally as one unit (0038).
