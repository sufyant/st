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
- **Memberships.** `memberships (tenant_id, user_id)`.
- **The access point.** `TenantCatalog` is bound to the scope's tenant. It filters tenant-owned rows by it and stamps it on the rows it adds, and it throws when used outside a tenant.
- **The one reader outside a tenant.** `TenantDirectory` (0015) reads memberships before any tenant is known, and does so deliberately.

## Alternatives considered

- **RLS on the catalog.** The catalog must be read before a tenant is known (tenant resolution, membership checks), and cross-tenant queries are its purpose.
- **Tenant-owned rows in tenant schemas.** Roles and memberships are needed to resolve and authorize a tenant in the first place.

## Consequences

- Accepted tension: for tenant-owned catalog rows, isolation is enforced in the application through the single access point and tests, not by the database.
- Code that reads tenant-owned catalog rows directly from the DbContext, outside `TenantCatalog` and `TenantDirectory`, bypasses that isolation; review has to catch it.
