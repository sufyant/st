# 0017. Tenant context in background work

- Status: Proposed
- Date: 2026-10-04

## Context

Handlers, saga steps and scheduled jobs run outside HTTP requests, yet they must not bypass RLS. Some jobs need to find work across all tenants.

## Decision

- Every message envelope carries the tenant id; the handler sets it on its transaction (0016).
- Jobs that find work across tenants (the Hangfire scanner, system cleanup) either fetch due items through a narrow `SECURITY DEFINER` function that returns only `(tenant_id, id)`, or iterate tenants from the catalog. Each item is then processed separately under its own tenant.
- No database role gets `BYPASSRLS`.

## Alternatives considered

- **A privileged role with `BYPASSRLS` for background work.** One bug exposes every tenant.
- **Scanners read full rows across tenants.** Wider exposure than the scan needs.

## Consequences

- Background work is isolated exactly like requests.
- Each `SECURITY DEFINER` function is a reviewed, minimal exception and must stay narrow.
