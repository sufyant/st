# 0005. Module project structure

- Status: Proposed
- Date: 2026-10-04

## Context

Module boundaries and layer rules are easiest to enforce when every module has the same shape. Jovanović's modular monolith guidance uses a fixed set of projects per module. Ousterhout warns against shallow layers that add interfaces without hiding complexity.

## Decision

Under `apps/api/`, modules are laid out flat, with no grouping folders such as ControlPlane/DataPlane:

```text
apps/api/
  Api.slnx
  package.json                         # build and test as Turborepo tasks (0046)
  docs/
    ARCHITECTURE.md
    adr/
  src/
    Modules/
      ControlPlane/
        ControlPlane.Contracts/        # the face offered to other modules
        ControlPlane.Domain/
        ControlPlane.Application/
        ControlPlane.Infrastructure/   # DbContext, migrations, catalog schema
        ControlPlane.Api/              # endpoints, request/response types, AddControlPlaneModule()
      Notifications/ ...
      Audit/ ...
    Shared/
      SharedKernel/
    Api/                               # the single executable host: Program.cs, pipeline
  tests/                               # see 0042
```

Every module has all five projects, even when some are thin, so the architecture rules are identical for every module.

## Alternatives considered

- **One project per module plus Contracts.** Fewer, deeper projects; layer rules move from project references to namespaces. Kept as the fallback.
- **Grouping folders (ControlPlane/DataPlane).** Extra nesting without a rule that depends on it.

## Consequences

- Accepted tension: five projects per module inflates the project count and creates shallow projects in thin modules such as Audit.
- The decision is easy to reverse: a module can collapse to one project plus Contracts.
