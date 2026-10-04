# 0012. EF Core code-first for data access

- Status: Proposed
- Date: 2026-10-04

## Context

Modules need migrations, a model in which tenant automation can be applied centrally (0014), and occasionally efficient read queries.

## Decision

- Data access uses EF Core code-first, one DbContext per module (0008).
- Dapper may be used for read queries where it is needed.

## Alternatives considered

- **Dapper only.** Fast and explicit, but no migrations and no model-wide query filter for tenant isolation.

## Consequences

- Tenant column, query filter and RLS policy generation hook into EF Core model building and migrations.
- Dapper reads bypass the EF Core query filter; RLS still protects them.
