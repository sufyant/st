# 0013. Shared database multi-tenancy with Row Level Security

- Status: Proposed
- Date: 2026-10-04

## Context

The template must scale to thousands of tenants. Golding and the Azure multitenant guidance describe three models: database per tenant, schema per tenant, and shared tables with row isolation.

## Decision

- One database with shared tables; tenant isolation by PostgreSQL Row Level Security.
- Tenant isolation is guaranteed by the database, not by application code.
- The tenant resolution layer is kept abstract so that one tenant can later be moved to its own database (a hybrid model).

## Alternatives considered

- **Database per tenant.** Strong isolation, but in practice operations stall at tens to a few hundred tenants.
- **Schema per tenant.** Carries the complexity of both other models without the isolation of separate databases.

## Consequences

- Isolation correctness rests on RLS policies and the tests that prove them (0014, 0042).
- Tenants share capacity; noisy neighbours are possible.
- No foreign keys between the catalog and tenant schemas (0021), so moving a tenant out stays possible.
