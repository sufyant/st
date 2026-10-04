# 0014. Two-layer tenant isolation derived from ITenantEntity

- Status: Proposed
- Date: 2026-10-04

## Context

A single forgotten filter in application code would leak data between tenants. Isolation should hold even when application code is wrong, and adding a tenant entity should not depend on remembering steps.

## Decision

A single marker, `ITenantEntity`, drives both layers:

1. **Application.** During EF Core model building, every `ITenantEntity` automatically gets a tenant column as a shadow property (the domain never sees it) and a global query filter.
2. **Database.** The same marker makes migrations emit an RLS policy with `USING` for reads and `WITH CHECK` for writes. The tenant column's default value comes from the active tenant setting.

If code forgets the filter, PostgreSQL still returns no wrong row and writes to no wrong tenant.

## Alternatives considered

- **Query filter only.** Fails silently on raw SQL, Dapper or a missing filter.
- **RLS only.** A mistake in a policy would have no second line of defence.
- **Hand-written policies per table.** Easy to forget for a new entity.

## Consequences

- Every `ITenantEntity` is automatically covered by the isolation test suite (0042); a tenant entity that is not covered means the build is wrong.
- Neither layer may be bypassed, disabled or worked around.
