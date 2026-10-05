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
- **No one hands out more than they hold.** Granting, changing or taking away a role is allowed only when every permission of the roles involved is the actor's own. This also covers shaping a custom role: what it held and what it will hold. So an admin can neither make an owner nor demote one, and nobody can give a permission they do not have.
- Object level is covered by RLS, function level by permissions, property level by separate response types (0010). Authorization behaviour is proven by tests.

### How it works

- **The catalogue lives in SharedKernel** (`Permissions`), as constants with two pools: tenant and system (0031).
  - SharedKernel is the only project every module layer and the host can see (0006). An endpoint in any module names its permission there, and the role rules in ControlPlane check against the same pool.
  - A permission is added there when its capability is built.
  - Phase 4 has these tenant permissions: `members.invite`, `members.manage`, `owners.manage`, `roles.manage`. Phase 6 adds `notifications.schedule`, scheduling notifications for oneself (0027).
- **Built-in roles.**
  - Owner holds the whole tenant pool. Admin holds all of it except `owners.manage`.
  - Modules give members and viewers permissions as they add capabilities for them. A member holds `notifications.schedule`. A viewer only reads, and no permission that only reads exists yet, so a viewer holds nothing.
  - Because nobody hands out more than they hold, inviting someone as a member takes every permission a member has. A custom role that holds only `members.invite` can invite viewers, but no longer members, once members hold `notifications.schedule`.
  - Built-in roles are stored once, with fixed ids, as `catalog.roles` rows that belong to no tenant. Their permissions come from code and the rows hold none. This way memberships and invitations reference every role through one foreign key.
- **Custom roles** are rows of their tenant in `catalog.roles`, with their permissions as a text array.
  - Names are unique in the tenant and cannot be a built-in role's name.
  - A role in use cannot be deleted: memberships reference roles with `RESTRICT`. Deleting a role with pending invitations is refused; the history of used invitations goes with the role.
- **Assignment.** A membership has exactly one role (`memberships.role_id`).
- **The last owner.** The `Membership` aggregate refuses to demote or remove the last owner, given the tenant's owner count.
  - Commands that change memberships or delete roles first lock the tenant's catalog row (`SELECT ... FOR UPDATE`) in their transaction. So two owners demoting each other at the same moment cannot both pass.
  - A test shows the second change waiting for the lock.
- **Authorization in the host.**
  - Tenant resolution (0015) reads the member's permissions together with the membership, in the same query, through `ITenantDirectory` (0048).
  - An endpoint names its permission as its authorization policy: `RequireAuthorization(Permissions.MembersInvite)`. A policy name that is not a registered policy is read as a permission. An unknown name therefore fails closed: no one holds it.
  - The handler checks the permission against the resolved membership, or against the system permissions on admin routes. The two pools never overlap.
- **Responses** are Problem Details (0032):
  - No user: 401.
  - A tenant route whose tenant was not resolved: 404 (0015).
  - A missing permission: 403.
  - A rule that refuses a grant: 403 `role.beyond_your_permissions`.
- **Endpoints of Phase 4**, under `/v1/tenants/{slug}`:
  - `POST/PUT/DELETE /roles[/{id}]` with `roles.manage`.
  - `PUT /members/{userId}/role` and `DELETE /members/{userId}` with `members.manage`.
  - `POST /invitations` with `members.invite` (0029).
  - Lists of roles, members and invitations are added when a client needs them.

## Alternatives considered

- **Role checks in code.** Custom roles would need code changes.
- **Tenant-defined permissions.** A permission without a capability behind it means nothing.
- **The catalogue in ControlPlane.Contracts.** Owned by the module that owns roles, but other modules' `X.Api` projects cannot reference it (0006), so their endpoints would name permissions as loose strings.
- **Any holder of `members.invite` or `members.manage` may grant any role.** Simpler, but an admin could make anyone an owner.
- **Built-in roles copied into every tenant.** Needs seeding on every tenant creation for data that is fixed in code anyway.
- **Optimistic concurrency for the last-owner rule.** Needs a version on a tenant-wide root and retries; a row lock is simpler for changes this rare.

## Consequences

- Custom roles work without code changes.
- The last-owner invariant is protected by the aggregate that owns memberships (Evans); the lock makes the owner count it is given hold until the change commits.
- Adding a permission touches SharedKernel, which otherwise holds no business vocabulary.
