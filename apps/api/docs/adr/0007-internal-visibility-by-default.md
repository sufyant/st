# 0007. Module types are internal by default

- Status: Proposed
- Date: 2026-10-04

## Context

Project references (0006) decide which assemblies can see each other; access modifiers decide which types inside them can be used. Public types invite use from places the architecture does not intend.

## Decision

- Types inside a module are `internal` by default. A type is public only when the module deliberately offers it outward.
- A module's own projects and its test projects may see each other's internal types through `InternalsVisibleTo`. Granting it to another module's project is forbidden; an architecture test enforces this (0006).
- If Wolverine's code generation needs public handlers or messages, a narrowly scoped exception ADR is written instead of making types public broadly.

## Alternatives considered

- **Public by default, relying on architecture tests.** The compiler stops helping, and tests catch only the rules someone wrote.
- **Public types within a module's own projects.** The five projects of a module (0005) would have to make most of their types public just to see each other, which would also expose them to other modules.

## Consequences

- The compiler enforces part of the boundary.
- Each module project lists its sibling projects and test projects in `InternalsVisibleTo`.
- Wolverine's discovery requires its handlers, messages and validators to be public; 0047 records that exception.

## Verified

- Phase 2, WolverineFx 6.45.0: Wolverine does not discover internal handler types, internal handler methods or handlers of internal messages. Its FluentValidation middleware skips internal validators without an error. The exception is recorded in 0047.
