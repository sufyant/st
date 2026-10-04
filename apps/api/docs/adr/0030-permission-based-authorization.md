# 0030. Permission-based authorization with built-in and custom roles

- Status: Proposed
- Date: 2026-10-04

## Context

Tenants need their own roles without code changes, while what a permission allows is defined by code. OWASP API Security Top 10 names object-level (BOLA), function-level (BFLA) and property-level (BOPLA) authorization failures.

## Decision

Three layers:

1. **Permission catalogue.** Fixed in code (for example `members.invite`). Tenants cannot create permissions, because a permission is tied to a real capability in code.
2. **Roles.** Built-in roles (owner, admin, member, viewer) exist in every tenant; their permissions are fixed in code and they cannot be deleted or edited. A tenant can create custom roles by choosing from the catalogue. Every tenant always has at least one owner; the last owner cannot be removed or demoted.
3. **Membership.** A role is assigned to a user in a tenant, not to the user. The same person can be admin in one tenant and viewer in another.

- Code checks permissions, never roles: "may this user do X", not "is this user an admin".
- Object level is covered by RLS, function level by permissions, property level by separate response types (0010). Authorization behaviour is proven by tests.

## Alternatives considered

- **Role checks in code.** Custom roles would need code changes.
- **Tenant-defined permissions.** A permission without a capability behind it means nothing.

## Consequences

- Custom roles work without code changes.
- The last-owner invariant is protected by the aggregate that owns memberships (Evans).
