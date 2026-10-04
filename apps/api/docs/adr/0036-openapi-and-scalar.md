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
- **Responses of `Result` endpoints.** The response type inferred from an endpoint that returns `Result` or `Result<T>` (0032) is the `Result` type itself, which no client receives. A convention on the version group, `DescribeResults`, replaces it with what the host actually sends:
  - `T` with 200 for `Result<T>`, 204 for `Result`.
  - Problem Details for the failures a `Result` can carry: 400 as validation Problem Details, and 403, 404 and 409.

## Alternatives considered

- **Swagger UI.** Replaced by Scalar.
- **Hand-written OpenAPI.** Drifts from the code.
- **Generating the client package in the API build.** Couples the API build to clients.

## Consequences

- The committed document must be kept current by the build.

## Verified

- Phase 4, with the first real endpoints: on its own the document described `Result<T>` as the response body and no error responses. With the convention above, a test verifies that it describes the real response type, 204 for results without a value, Problem Details for failures, and that no `Result` schema remains.
