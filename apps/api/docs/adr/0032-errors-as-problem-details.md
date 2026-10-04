# 0032. Errors as Problem Details; expected failures as results

- Status: Proposed
- Date: 2026-10-04

## Context

Clients should interpret every error in one place. Using exceptions for expected business outcomes hides control flow and leaks internals when mishandled.

## Decision

- Every error is returned as Problem Details (RFC 9457).
- **Expected failures** ("email already registered", "not found", "not allowed") are not exceptions. The handler returns a Result; the host maps it to the right HTTP status and Problem Details.
- **Unexpected failures** are caught by one global exception handler: a safe generic message to the user, full detail in the logs. Stack traces never leave the server.
- No try/catch in handlers for control flow.
- Endpoints return the `Result` of their command; an endpoint filter on the version group maps it once for every endpoint:

  | Result | HTTP |
  | --- | --- |
  | Success with a value | 200 with the value |
  | Success without a value | 204 |
  | `ErrorType.Validation` | 400 |
  | `ErrorType.NotFound` | 404 |
  | `ErrorType.Conflict` | 409 |
  | `ErrorType.Forbidden` | 403 |

  The Problem Details carry the error's description as `detail` and its code as a `code` extension. Every Problem Details response carries a `traceId` extension.
- A command rejected by its validator (0022) becomes a 400 validation Problem Details listing the invalid fields. Wolverine's middleware signals it by throwing, and an exception handler in the host maps it; handlers still contain no try/catch.
- Unknown routes and other empty error responses are Problem Details too.

## Alternatives considered

- **Exceptions for business failures.** Hidden control flow and costly.
- **Custom error envelope.** A private format instead of a standard one.

## Consequences

- Result and error types live in SharedKernel (0004).
- Clients handle a single error format.
