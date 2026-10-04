# 0015. Tenant resolution from the path slug and verified membership

- Status: Proposed
- Date: 2026-10-04

## Context

Each tenant-scoped request must run under exactly one tenant, and that tenant must never be chosen by trusting the client.

## Decision

1. The request arrives and its Clerk token is validated (0028).
2. Middleware takes the tenant slug from the path (`/v1/tenants/{slug}/...`, 0033). It resolves the slug to the internal tenant id and verifies, against the catalog, that the user is a member of that tenant. Inside the system the id travels, not the slug.
3. The tenant is then set for the request: on the scope's `TenantContext`, and on the request's Wolverine message bus, so commands sent from the request carry it in their envelope. The transaction that sets it on the database belongs to the message (0016).

### How it works

- The user is identified by the `ClaimTypes.NameIdentifier` claim, which carries the identity provider's user id. That id matches `users.external_id` in the catalog (0021).
- Only an active tenant resolves. A tenant that is provisioning or failed does not resolve, for its members either.
- The lookup is behind `ITenantDirectory` (0048), implemented by ControlPlane. The host does not know ControlPlane, and the resolution layer stays replaceable (0013).
- Order in the host pipeline:
  1. Routing.
  2. Tenant resolution, which records the outcome but rejects nothing.
  3. Rate limiting (0035), so non-members are limited by user or address.
  4. A filter on the tenant route group that rejects unresolved requests.
- **Rejection.** Without a user the response is 401. With a user, an unknown slug, a tenant the user is not a member of and an inactive tenant all give 404, so a tenant the user cannot enter looks the same as one that does not exist. Both are Problem Details (0032).
- **Observability.** A resolved tenant's id is added to the request log, the log context, the trace (`tenant.id`) and the HTTP metrics (0039).

### Other rules

- The membership query runs on every request and is not cached until measurement shows a need.
- Slugs cannot be changed for now; renaming with redirects from old URLs is added when needed.
- Moving to subdomains later means replacing the resolution layer only.

## Alternatives considered

- **Tenant id in a header or body.** A client declaration, not a verified fact.
- **Tenant from the identity provider's organization claim.** Clerk Organizations are not used (0028).
- **Subdomain per tenant.** Possible later through the same resolution layer.
- **Caching membership.** Deferred until measured (YAGNI).
- **Reject in the resolution middleware.** Simpler, but non-members would never reach the rate limiter, and the membership lookup could be driven without limit.
- **403 for a non-member.** Tells the caller that the tenant exists.

## Consequences

- One catalog lookup per tenant-scoped request.
- Slug changes need a later decision.
- Until Clerk authentication is in place (0028), no request carries a user, so every tenant route answers 401.
