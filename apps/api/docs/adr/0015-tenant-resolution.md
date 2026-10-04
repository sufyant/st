# 0015. Tenant resolution from the path slug and verified membership

- Status: Proposed
- Date: 2026-10-04

## Context

Each tenant-scoped request must run under exactly one tenant, and that tenant must never be chosen by trusting the client.

## Decision

1. The request arrives and its Clerk token is validated (0028).
2. Middleware takes the tenant slug from the path (`/v1/{tenant-slug}/...`), resolves it to the internal tenant id, and verifies the user's membership in that tenant against the catalog. Inside the system the id travels, not the slug.
3. The tenant is then set on the request's transaction (0016).

- The membership query runs on every request and is not cached until measurement shows a need.
- Slugs cannot be changed for now; renaming with redirects from old URLs is added when needed.
- Moving to subdomains later means replacing the resolution layer only.

## Alternatives considered

- **Tenant id in a header or body.** A client declaration, not a verified fact.
- **Tenant from the identity provider's organization claim.** Clerk Organizations are not used (0028).
- **Subdomain per tenant.** Possible later through the same resolution layer.
- **Caching membership.** Deferred until measured (YAGNI).

## Consequences

- One catalog lookup per tenant-scoped request.
- Slug changes need a later decision.
