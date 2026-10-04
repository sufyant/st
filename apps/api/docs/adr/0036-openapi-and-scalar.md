# 0036. OpenAPI as the single source, Scalar UI, committed document

- Status: Proposed
- Date: 2026-10-04

## Context

Clients need an accurate API description, and API changes should be visible in review.

## Decision

- The OpenAPI document is generated from the Minimal API endpoints; XML comments feed the descriptions.
- Scalar is the API reference UI.
- The OpenAPI document is committed under `apps/api` so that API changes show up in review.
- Generating client types is each client's own concern.

## Alternatives considered

- **Swagger UI.** Replaced by Scalar.
- **Hand-written OpenAPI.** Drifts from the code.
- **Generating the client package in the API build.** Couples the API build to clients.

## Consequences

- The committed document must be kept current by the build.
