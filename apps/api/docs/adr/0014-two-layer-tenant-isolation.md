# 0014. Two-layer tenant isolation derived from ITenantEntity

- Status: Proposed
- Date: 2026-10-04

## Context

A single forgotten filter in application code would leak data between tenants. Isolation should hold even when application code is wrong, and adding a tenant entity should not depend on remembering steps.

## Decision

A single marker, `ITenantEntity` in SharedKernel, drives both layers. The plumbing lives in the Tenancy project (0048).

1. **Application.** A module DbContext that holds tenant entities derives from `TenantDbContext`. During model building, every `ITenantEntity` automatically gets:
   - A shadow property `TenantId`, mapped to the column `tenant_id` and indexed; the domain never sees it.
   - A global query filter, named `Tenant`, that compares the column with the scope's `TenantContext`. Without a tenant, the filter matches no row.
2. **Database.** The same marker makes migrations emit row level security for the table:
   - The tenant column defaults to the active tenant setting: `NULLIF(current_setting('app.tenant_id', true), '')::uuid`.
   - The table gets `ENABLE ROW LEVEL SECURITY` and one policy, `tenant_isolation`, for all commands. The policy has `USING` for reads and `WITH CHECK` for writes, both `tenant_id = <active tenant>`.
   - A custom migrations SQL generator adds both statements wherever a table is created with the tenant column, or the column is added to an existing table. It recognises the column by its default, which only `TenantDbContext` gives, because migration operations do not carry custom model annotations. The statements appear in `dotnet ef migrations script` output.

If code forgets the filter, PostgreSQL still returns no wrong row and writes to no wrong tenant. Without an active tenant, reads return nothing and writes are rejected by `WITH CHECK`.

### Details

- **`FORCE ROW LEVEL SECURITY` stays off.** Forcing would subject the table owner to the policy too. The narrow `SECURITY DEFINER` lookups of 0017 run as the owner precisely so that they can see across tenants. Under `FORCE` they would see nothing, unless a policy let the owner see every tenant, and 0018 rules out such a policy. The owner credential is used only by the migration step (0020). The application role owns no table (0018), so the policy always applies to it.
- **`NULLIF` is needed.** Once a transaction has set `app.tenant_id`, the session keeps the setting as an empty string after the transaction ends, and `''::uuid` would fail with a cast error instead of meaning "no tenant".
- **The readiness check enforces the role.** Row level security does not hold for a superuser, for a role with `BYPASSRLS`, or for a table's owner. `/health/ready` therefore checks the role it is connected as: it must not be a superuser, must not have `BYPASSRLS`, and must own no table. Otherwise the pod is reported unhealthy and receives no traffic. A misconfigured connection string fails loudly instead of silently disabling isolation.

## Alternatives considered

- **Query filter only.** Fails silently on raw SQL, Dapper or a missing filter.
- **RLS only.** A mistake in a policy would have no second line of defence.
- **Hand-written policies per table.** Easy to forget for a new entity.
- **`FORCE ROW LEVEL SECURITY`.** Also protects against queries run as the owner, but breaks the `SECURITY DEFINER` lookups (0017), or requires a policy that sees all tenants.
- **Mark tenant tables with a model annotation.** Clearer than recognising the column default, but annotations do not reach the migration operations the SQL generator receives.

## Consequences

- Every `ITenantEntity` is automatically covered by the isolation test suite (0042); a tenant entity that is not covered means the build is wrong.
- Neither layer may be bypassed, disabled or worked around.
- Queries run as the owner, such as data migrations, see every tenant; only the query filter applies to them.
- The SQL generator derives from Npgsql's generator, whose constructor takes an option type from Npgsql's internal namespace; a change there can break the build when Npgsql is upgraded.
