# 0021. Catalog schema without RLS and a tenant-scoped access point

- Status: Proposed
- Date: 2026-10-04

## Context

Some data sits above tenants: the tenants themselves, users who can belong to several tenants, and system admins. Golding notes that the control plane itself is not multi-tenant.

## Decision

- `catalog` is the single schema above tenants, owned by ControlPlane. It holds tenants (slug, status), users (one identity matched to the Clerk identity, member of any number of tenants), tenant-user memberships, roles, invitations and system admins.
- The catalog has no RLS.
- Catalog rows that belong to a tenant (roles, memberships, invitations) are reached only through one tenant-scoped access point, which is covered by isolation tests.
- No foreign keys between the catalog and tenant schemas; consistency is kept in the application, so a tenant can later move to its own database.

## Alternatives considered

- **RLS on the catalog.** The catalog must be read before a tenant is known (tenant resolution, membership checks), and cross-tenant queries are its purpose.
- **Tenant-owned rows in tenant schemas.** Roles and memberships are needed to resolve and authorize a tenant in the first place.

## Consequences

- Accepted tension: for tenant-owned catalog rows, isolation is enforced in the application through the single access point and tests, not by the database.
