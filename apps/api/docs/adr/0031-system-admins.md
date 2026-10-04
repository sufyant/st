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
- Cross-tenant reports use only the read-only reporting role (0018), only from admin endpoints. That role reads the catalog only.
- The admin API is a separate route group in the same host with its own authorization policy. The Hangfire dashboard is open only in local development for now (0027).
- MFA is mandatory for system admins and is enforced by the API: the admin route group requires second-factor verification in the token.

### How it works

- **The grant.** `catalog.system_admins` holds `user_id`, `role`, `granted_by` and `granted_at`.
  - There is one system role, `Administrator`, with the whole system pool: `system.tenants.read`, `system.tenants.enter`, `system.members.invite`.
  - More system roles come when a need appears.
- **The seed script.** `apps/api/db/seed-system-admin.sql` runs after the migrations:
  - `psql -v external_id=<Clerk user id> -f seed-system-admin.sql`.
  - It creates the catalog user if missing (`uuidv7()`), then the grant with no `granted_by`.
  - It can be run again.
- **Route groups in the host.**
  - `/v1/admin` reads the user's system permissions through `ISystemAdminDirectory` (0048). It needs a system admin and the second-factor claim, which only the host's reading of `fva` sets; a claim of that name inside the token is discarded (0028).
  - `/v1/admin/tenants/{slug}` additionally needs `system.tenants.enter`. It resolves the tenant by slug without membership and whatever its status. It sets the tenant on the request's `TenantContext` and message bus exactly as tenant resolution does (0015, 0016), so RLS applies as usual.
- **Rejection.**
  - Without a second factor, or without a grant: 403.
  - An unknown slug: 404, but only for an admin who passes every other check; others cannot tell whether a slug exists.
- **Recording entries.** An endpoint filter on the admin tenant group runs after authorization and records every entry as a security event: `SystemAdminTenantEntry` in the `Api.Security` log category, with the user, the tenant and the request. The audit record (0040) joins it there once the Audit module exists (Phase 6).
- **Rate limiting.** A system admin inside a tenant is not its member and spends their own limit, not the tenant's (0035).
- **Endpoints of Phase 4.**
  - `GET /v1/admin/tenants?page&pageSize` (`system.tenants.read`): every tenant with its status and member count, paginated (0034).
    - It reads the catalog as the reporting role over `ConnectionStrings:Reporting` (0018, 0019), never as the application.
  - `POST /v1/admin/tenants/{slug}/invitations` (`system.members.invite`): invites someone with any role, for example a tenant's first owner (0029).

## Alternatives considered

- **An admin flag on `users`.** Mixes provider staff rights into tenant identity data and gives no history of who granted what.
- **A separate admin application or database role that bypasses RLS.** Contradicts 0017.

## Consequences

- Admins see tenant data only the way the tenant itself would.
- The admin surface is a distinct, separately authorized part of the API.
- MFA does not depend on a setting in the identity provider alone.
- Until the Audit module exists, entries are recorded only in the log.

## Verified

- Phase 4: Clerk reports factor verification in the session token's `fva` claim (session token version 2). It is an array of the minutes since the first and the second factor were verified; the second value is `-1` when no second factor was verified. The host treats a second value of 0 or more as a verified second factor.
