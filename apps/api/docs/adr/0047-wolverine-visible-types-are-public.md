# 0047. Types Wolverine discovers are public

- Status: Proposed
- Date: 2026-10-04

## Context

Module types are internal by default (0007), and 0007 asks for a narrow exception if Wolverine's code generation needs public types. Verification in Phase 2 against WolverineFx 6.45.0 showed:

- Handler discovery skips non-public handler types and non-public handler methods, and `Discovery.IncludeType` rejects a non-public type with an exception. Invoking a message whose only handler is internal fails with `IndeterminateRoutesException`.
- The FluentValidation middleware registers only public validators. An internal validator is skipped without any error, so an invalid command reaches its handler.
- A public handler cannot take an internal message or an internal service as a parameter: C#'s consistent accessibility rule rejects it at compile time.

## Decision

- In `X.Application`, Wolverine handlers, the messages they handle and FluentValidation validators are public.
- Handler methods on public handler types are public as well; Wolverine skips non-public handler methods the same way.
- Types that appear in a public handler's signature (injected service interfaces, for example) are public as a consequence. Domain types do not appear in handler signatures and stay internal.
- Everything else stays internal (0007). Implementations registered in the container behind those interfaces may stay internal.
- Architecture tests require every type in `X.Application` named `*Handler` or `*Consumer` (Wolverine's discovery suffixes), the handler methods on those types (`Handle`, `Consume` and their variants), and every validator there, to be public. The validator rule matters most, because Wolverine fails silently on an internal validator.
- Middleware types in the host that Wolverine's generated code calls are public too; nothing references the host, so this exposes nothing.

## Alternatives considered

- **Make all of `X.Application` public.** A simpler rule, but it gives up the compiler's help for every type Wolverine never sees.
- **Grant `InternalsVisibleTo` to Wolverine's generated assembly.** Discovery filters non-public types before code generation, so it would not help. It would also break the rule that internals are shared only within a module (0006).
- **A hand-written mediator.** Gives up what Wolverine provides (0023).

## Consequences

- Other modules still cannot reach these types: the reference rules and their architecture tests (0006) forbid any module from depending on another module's `X.Application`.
- The boundary inside a module (Application versus Infrastructure, Domain and Api) is weaker for these types, because the compiler no longer hides them.
- A handler or validator added with the wrong visibility fails the architecture tests instead of being skipped at runtime.
- Saga classes (Phase 5, 0025) fall under the same rule, and the architecture tests are extended to them when the first saga is written.
