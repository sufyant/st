# 0031. System admins

- Status: Proposed
- Date: 2026-10-04

## Context

The SaaS provider's own staff (system admins, in Golding's terms) must manage tenants and, when needed, look inside a tenant without weakening isolation.

## Decision

- A system admin is not a separate kind of person; it is an extra grant on the single identity.
- Grants live in a separate catalog table, `system_admins` (user, system role, granted by, granted at). The `users` table gets no flag.
- The first system admin is created by a seed script during setup.
- System permissions (for example `system.tenants.suspend`) form a separate pool. Tenant custom roles cannot select them.
- To see tenant data, an admin enters the tenant context: the chosen tenant is set on the transaction and RLS applies as usual. Every entry is audited (0040).
- Cross-tenant reports use only the read-only reporting role (0018), only from admin endpoints.
- The admin API is a separate route group in the same host with its own authorization policy. The Hangfire dashboard also requires a system admin permission.
- MFA is mandatory for system admins.

## Alternatives considered

- **An admin flag on `users`.** Mixes provider staff rights into tenant identity data and gives no history of who granted what.
- **A separate admin application or database role that bypasses RLS.** Contradicts 0017.

## Consequences

- Admins see tenant data only the way the tenant itself would.
- The admin surface is a distinct, separately authorized part of the API.
