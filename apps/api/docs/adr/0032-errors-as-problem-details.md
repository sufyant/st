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

## Alternatives considered

- **Exceptions for business failures.** Hidden control flow and costly.
- **Custom error envelope.** A private format instead of a standard one.

## Consequences

- Result and error types live in SharedKernel (0004).
- Clients handle a single error format.
