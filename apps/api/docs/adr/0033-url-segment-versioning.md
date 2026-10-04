# 0033. URL segment API versioning from day one

- Status: Proposed
- Date: 2026-10-04

## Context

Mobile and other clients can stay on old versions for a long time; the API must not break them.

## Decision

- The API is versioned from the first day, by URL segment.
- Tenant-scoped routes: `/v1/tenants/{slug}/...`. Routes outside a tenant: for example `/v1/me`, `/v1/invitations/...`.
- Tenant routes have a segment of their own, `tenants`, so a slug can never collide with a route outside a tenant. No slug is reserved, and adding a route outside tenants never makes an existing tenant unreachable.
- The API does not break older clients.
- A version is a route group (`/v1`) in the host, without a versioning library. The group also carries the result mapping (0032) and rate limiting (0035). A second version would be a second group.
- Infrastructure endpoints (health checks, the OpenAPI document and its UI) are not versioned.

## Alternatives considered

- **Header or media-type versioning.** Less visible, harder to route and to test by hand.
- **Tenant slug directly after the version (`/v1/{slug}/...`).** Shorter, but every route outside tenants (`me`, `invitations`, `admin`) would have to be a reserved slug, and a new such route could shadow an existing tenant.
- **Version later.** Retrofitting versions breaks existing clients.

## Consequences

- Breaking changes require a new version segment.
- Tenant URLs are one segment longer.
