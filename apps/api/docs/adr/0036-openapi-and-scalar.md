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
- The document is generated during `dotnet build` (`Microsoft.Extensions.ApiDescription.Server`) into `apps/api/openapi/v1.json`.
- The live document (`/openapi/v1.json`) and Scalar (`/scalar`) are served only in the Development environment; the committed document is the published description.

## Alternatives considered

- **Swagger UI.** Replaced by Scalar.
- **Hand-written OpenAPI.** Drifts from the code.
- **Generating the client package in the API build.** Couples the API build to clients.

## Consequences

- The committed document must be kept current by the build.

## To verify

- Endpoints return `Result<T>`, which the host maps to the HTTP response (0032), so the generated document may not describe the real response type and the error responses on its own. With the first real endpoint (Phase 3), a test verifies that the document describes both correctly.
