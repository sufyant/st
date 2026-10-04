# 0008. Each module owns its data

- Status: Proposed
- Date: 2026-10-04

## Context

Modules are bounded contexts (Evans; Vernon; Khononov). Shared tables and cross-module foreign keys couple modules at the data level, where coupling is hardest to undo, and prevent extracting a module or moving a tenant to its own database later.

## Decision

- Each module owns its own PostgreSQL schema, its own DbContext and its own migrations.
- No foreign keys across schemas. Consistency across modules is handled in the application, by default through integration events (0009).
- No module reads or writes another module's schema.

## Alternatives considered

- **One shared DbContext and migration set.** Simpler at first; every module can then reach every table.
- **Foreign keys across schemas.** Database-level integrity, but modules can no longer be extracted or tenants moved independently.

## Consequences

- Cross-module integrity is eventual and must be designed per case.
- Each module's migrations evolve independently.
