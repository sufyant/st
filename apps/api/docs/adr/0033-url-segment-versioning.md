# 0033. URL segment API versioning from day one

- Status: Proposed
- Date: 2026-10-04

## Context

Mobile and other clients can stay on old versions for a long time; the API must not break them.

## Decision

- The API is versioned from the first day, by URL segment.
- Tenant-scoped routes: `/v1/{tenant-slug}/...`. Routes outside a tenant: for example `/v1/me`, `/v1/invitations/...`.
- The API does not break older clients.

## Alternatives considered

- **Header or media-type versioning.** Less visible, harder to route and to test by hand.
- **Version later.** Retrofitting versions breaks existing clients.

## Consequences

- Breaking changes require a new version segment.
