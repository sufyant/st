# 0028. Clerk for authentication only, in Restricted mode

- Status: Proposed
- Date: 2026-10-04

## Context

Authentication is a solved problem better bought than built. Authorization data (tenants, roles, memberships) is core to the product and must stay ours so the provider remains replaceable.

## Decision

- Clerk only authenticates identities.
- Clerk runs in Restricted sign-up mode: public sign-up is closed and accounts are created by invitation only (0029).
- Which tenant a user belongs to, and their roles and permissions, live in our `catalog` schema. Clerk Organizations are not used.
- Identity is token-centric; all clients come through the same door.

## Alternatives considered

- **Clerk Organizations for tenants and roles.** Puts core authorization data in the provider and ties us to it.
- **Self-hosted identity.** Running and securing identity ourselves is not worth it for the template.

## Consequences

- The provider can be replaced without touching authorization.
- Users and memberships must be kept in our catalog (0029).
