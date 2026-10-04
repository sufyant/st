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

### How it works

- **Validation.** The host validates Clerk session tokens with ASP.NET Core's JWT bearer handler (`Microsoft.AspNetCore.Authentication.JwtBearer`, MIT). It checks:
  - The issuer: `Clerk:Issuer`, the instance's Frontend API URL. The signing keys come from the issuer's OpenID configuration.
  - The signature and the lifetime, with five seconds of clock skew, as Clerk's own SDKs allow. Session tokens live a minute.
  - The authorized party (`azp`). When `Clerk:AuthorizedParties` lists origins and the token carries `azp`, it must be one of them. Tokens of clients without an origin, such as mobile apps, carry none.
  - Outside Development the list must not be empty. Otherwise `/health/ready` reports the pod unhealthy (0038), because a token issued to any origin of the Clerk instance would be accepted. In Development an empty list is allowed.
  - Session tokens have no audience, so none is checked.
- **The user.** The token's `sub` is the Clerk user id. It becomes the `ClaimTypes.NameIdentifier` claim that tenant resolution reads (0015).
- **Second factor.** The `fva` claim reports a second factor verified in the session (0031); the host turns it into a claim of its own, `second_factor_verified`. Validation first removes any claim of that name that the token itself carries, for example through a custom session claim, so only the host's reading of `fva` can set it.
- **Configuration is checked on use.** Like the connection strings (0019), it is not checked at start, because the build starts the host without configuration. Without an issuer no token validates, so every request stays anonymous and is refused where a user is needed.
- **Every versioned endpoint needs a user.** The `/v1` group requires an authenticated user. An endpoint that should be public says so explicitly. Health checks, the OpenAPI document and Scalar sit outside the group.
  - This replaces a global fallback policy. A fallback policy also applies to requests that match no route, which would turn an unknown route's 404 into a 401.

## Alternatives considered

- **Clerk Organizations for tenants and roles.** Puts core authorization data in the provider and ties us to it.
- **Self-hosted identity.** Running and securing identity ourselves is not worth it for the template.
- **Clerk's .NET SDK for validation.** It adds a dependency for what the framework's JWT bearer handler already does.

## Consequences

- The provider can be replaced without touching authorization.
- Users and memberships must be kept in our catalog (0029).
- Tests sign tokens shaped like Clerk's with a key of their own, and the host trusts that key in place of Clerk's (0042). Everything else about validation is the production code.

## Verified

- Phase 4: Clerk's "Restricted" sign-up mode is now called "Invite-only". Sign-up then needs an invitation ticket.
