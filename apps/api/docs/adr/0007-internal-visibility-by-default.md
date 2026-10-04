# 0007. Module types are internal by default

- Status: Proposed
- Date: 2026-10-04

## Context

Project references (0006) decide which assemblies can see each other; access modifiers decide which types inside them can be used. Public types invite use from places the architecture does not intend.

## Decision

- Types inside a module are `internal` by default. A type is public only when the module deliberately offers it outward.
- If Wolverine's code generation needs public handlers or messages, a narrowly scoped exception ADR is written instead of making types public broadly.

## Alternatives considered

- **Public by default, relying on architecture tests.** The compiler stops helping, and tests catch only the rules someone wrote.

## Consequences

- The compiler enforces part of the boundary.
- Wolverine's discovery and code generation may conflict with internal types (see To verify).

## To verify

- Whether Wolverine handler discovery and code generation work with `internal` handlers and messages (Phase 2). Record the result here, or write the exception ADR.
