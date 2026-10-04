# 0006. Module and layer reference rules enforced by tests

- Status: Proposed
- Date: 2026-10-04

## Context

In a monolith nothing physical stops one module from using another's internals. Rules kept only in documents erode. Ford, Parsons and Kua (*Building Evolutionary Architectures*) call automated checks of such rules fitness functions.

## Decision

Projects may reference only the following:

| Project | May reference |
| --- | --- |
| `X.Domain` | The .NET base library and SharedKernel; no third-party packages |
| `X.Contracts` | The .NET base library and SharedKernel; no third-party packages; carries no transitive dependency to other modules |
| `X.Application` | `X.Domain`, `X.Contracts`, other modules' `*.Contracts`, SharedKernel |
| `X.Infrastructure` | `X.Application`, `X.Domain`, `X.Contracts`, SharedKernel, Tenancy (0048) |
| `X.Api` | `X.Application`, `X.Contracts`, `X.Infrastructure` (only to register `AddXModule()`), SharedKernel |
| `Api` host | Each module's `X.Api` project, SharedKernel to map results to Problem Details (0032), and Tenancy for the tenant pipeline (0048) |
| SharedKernel | The .NET base library only |
| Tenancy | SharedKernel, and the persistence packages named in 0048 |

- A module reaches another module only through that module's `*.Contracts`.
- Contracts may reference SharedKernel because SharedKernel has no dependencies (0022), so a synchronous contract can return SharedKernel's Result and error types without pulling anything else into its callers.
- `X.Api` references `X.Infrastructure` only to register the module. A type-level rule enforces this: inside `X.Api`, only the module registration class, named `XModule` and providing `AddXModule()`, may use types from `X.Infrastructure`.
- `InternalsVisibleTo` is allowed only between a module's own projects and its test projects, never between modules (0007). SharedKernel, Tenancy and the host expose internals only to their own test projects.
- The rules are encoded as tests in `Architecture.Tests`; a violation breaks the build. A rule is never weakened to make code compile. Each rule is checked in two ways:
  - Type dependencies are checked with NetArchTest. The base-library-only rules (SharedKernel, `X.Contracts`, `X.Domain`) are checked against each assembly's references, because a namespace cannot tell a framework type from a third-party one.
  - Project references are checked against the table as written in the project files, read from the test project's deps file. A type check sees only references some type uses; an unused reference would let the next change use it without anyone noticing.

## Alternatives considered

- **Rules by convention and review.** Erodes under deadline pressure; violations surface late.
- **Separate repositories or packages per module.** Hard boundaries at the cost of versioning and release overhead the template does not need.

## Consequences

- Project references alone cannot express the registration-only rule, so it needs the type-level test above.
- A project reference that breaks the table fails the build even before any code uses it.
- Boundary violations fail fast and visibly.
- The architecture tests must stay green at all times.
