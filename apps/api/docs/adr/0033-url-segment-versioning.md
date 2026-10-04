# 0033. URL segment API versioning from day one

- Status: Proposed
- Date: 2026-10-04

## Context

Mobile and other clients can stay on old versions for a long time; the API must not break them.

## Decision

- The API is versioned from the first day, by URL segment.
- Tenant-scoped routes: `/v1/{tenant-slug}/...`. Routes outside a tenant: for example `/v1/me`, `/v1/invitations/...`.
- The API does not break older clients.
- A version is a route group (`/v1`) in the host, without a versioning library. The group also carries the result mapping (0032) and rate limiting (0035). A second version would be a second group.
- Infrastructure endpoints (health checks, the OpenAPI document and its UI) are not versioned.

## Alternatives considered

- **Header or media-type versioning.** Less visible, harder to route and to test by hand.
- **Version later.** Retrofitting versions breaks existing clients.

## Consequences

- Breaking changes require a new version segment.
