# 0021. Catalog schema without RLS and a tenant-scoped access point

- Status: Proposed
- Date: 2026-10-04

## Context

Some data sits above tenants: the tenants themselves, users who can belong to several tenants, and system admins. Golding notes that the control plane itself is not multi-tenant.

## Decision

- `catalog` is the single schema above tenants, owned by ControlPlane. It holds:
  - Tenants (slug, status).
  - Users: one identity matched to the Clerk identity, member of any number of tenants.
  - Tenant-user memberships.
  - Roles, invitations and system admins.
- The catalog has no RLS.
- Catalog rows that belong to a tenant (roles, memberships, invitations) are reached only through one tenant-scoped access point, which is covered by isolation tests.
- No foreign keys between the catalog and tenant schemas; consistency is kept in the application, so a tenant can later move to its own database. Foreign keys inside the catalog are allowed.

### What exists so far

- **Tenants.** `tenants (id, slug, status)`.
  - The slug is unique and URL-safe: 3 to 63 lowercase letters, digits and single hyphens, starting and ending with a letter or digit. The `Tenant` aggregate enforces it.
  - The status is `Provisioning`, `Active` or `Failed` (0026); only an active tenant resolves (0015).
- **Users.** `users (id, external_id)`. `external_id` is the identity provider's user id and is unique.
- **Memberships.** `memberships (tenant_id, user_id, role_id)`; the role is a foreign key to `roles` (0030).
- **Roles.** `roles (id, tenant_id, name, built_in, permissions)`. Built-in roles are rows without a tenant, shared by every tenant; custom roles belong to one (0030).
- **Invitations.** `invitations (id, tenant_id, email, role_id, token_hash, invited_by, created_at, expires_at, status, accepted_at, accepted_by)` (0029). `token_hash` stays empty until the invitation is delivered.
- **System admins.** `system_admins (user_id, role, granted_by, granted_at)` (0031).
- **The access point.** `TenantCatalog` is bound to the scope's tenant. It is the implementation of the `ITenantCatalog` port that ControlPlane's handlers use (0047).
  - It reads only the active tenant's rows, plus the built-in roles, which belong to no tenant.
  - It stamps the active tenant on the memberships it adds. It refuses to add or remove a role or an invitation of another tenant.
  - It adds a tenant only as the active tenant itself, which its onboarding creates (0026), and only if no other tenant has its slug. The slug's unique index decides, also between two onboardings at the same moment, and nothing else about the other tenant is revealed.
  - It throws when used outside a tenant.
- **The readers outside a tenant.** They read deliberately, before any tenant is known, and each reveals as little as its caller needs:
  - `TenantDirectory` (0015) reads memberships with their role's permissions, and finds a tenant by slug for a system admin entering it (0031).
  - `InvitationDirectory` finds the tenant of an invitation from its token, so that accepting it can run in that tenant (0029). It reveals only the tenant id.
  - `SystemAdminDirectory` reads a user's system permissions; system admin grants belong to no tenant.

## Alternatives considered

- **RLS on the catalog.** The catalog must be read before a tenant is known (tenant resolution, membership checks), and cross-tenant queries are its purpose.
- **Tenant-owned rows in tenant schemas.** Roles and memberships are needed to resolve and authorize a tenant in the first place.

## Consequences

- Accepted tension: for tenant-owned catalog rows, isolation is enforced in the application through the single access point and tests, not by the database.
- Code that reads tenant-owned catalog rows directly from the DbContext, outside `TenantCatalog` and the readers above, bypasses that isolation; review has to catch it.
