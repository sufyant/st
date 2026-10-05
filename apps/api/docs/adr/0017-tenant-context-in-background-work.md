# 0017. Tenant context in background work

- Status: Proposed
- Date: 2026-10-04

## Context

Handlers, saga steps and scheduled jobs run outside HTTP requests, yet they must not bypass RLS. Some jobs need to find work across all tenants.

## Decision

- **Every message envelope carries the tenant id.** The handler's transaction sets it (0016). This is Wolverine's own envelope tenant id: a message sent with a tenant (`InvokeForTenantAsync`, `DeliveryOptions.TenantId`) carries it, and a message cascaded from a handler inherits the tenant of the message being handled.
- **A stored message keeps its tenant.** The tenant id is part of the stored envelope (0024), so a message recovered after a crash runs under its tenant. A message sent without a tenant comes back with Wolverine's default tenant id, which names no tenant (0016).
- **Jobs that find work across tenants** (the Hangfire scanner, system cleanup) either fetch due items through a narrow `SECURITY DEFINER` function that returns only `(tenant_id, id)`, or iterate tenants from the catalog. Each item is then processed separately under its own tenant.
- **No database role gets `BYPASSRLS`.**

### Shape of a `SECURITY DEFINER` lookup

`TenantScan.CreateFunctionSql` (0048) writes the function from a migration:

- The function belongs to the owner, which runs the migrations.
- It is `LANGUAGE sql STABLE SECURITY DEFINER`, with `search_path` pinned to `pg_catalog, pg_temp`.
- It returns `TABLE (tenant_id uuid, id uuid)` for the rows that meet the condition given in the migration.
- `EXECUTE` is revoked from `PUBLIC` and granted to the application role only.

It sees every tenant because the owner owns the table and row level security is not forced (0014). Callers learn which items are due, never their content.

## Alternatives considered

- **A privileged role with `BYPASSRLS` for background work.** One bug exposes every tenant.
- **Scanners read full rows across tenants.** Wider exposure than the scan needs.
- **A tenant id inside each message body.** Duplicates the envelope and is easy to forget on a new message.

## Consequences

- Background work is isolated exactly like requests.
- Each `SECURITY DEFINER` function is a reviewed, minimal exception and must stay narrow.
- The first real scanner is the Notifications module's (Phase 6): `notifications.due_scheduled_notifications(due_at_or_before)` returns the tenant and id of every scheduled notification that is due, and each is then sent under its own tenant (0027, 0037). The shape is also proven on a test table in `Tenancy.IntegrationTests`.
