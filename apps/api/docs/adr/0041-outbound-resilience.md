# 0041. Resilience on outbound calls

- Status: Proposed
- Date: 2026-10-04

## Context

Every external call can fail or hang (Nygard, *Release It!*).

## Decision

- Outbound HTTP clients use timeouts, retries and circuit breakers through Microsoft.Extensions.Http.Resilience.

### How it works

- Every typed client gets the standard resilience handler (`AddStandardResilienceHandler`): a total timeout, retries with exponential back-off and jitter on transient failures (5xx, 408, 429 and network errors), a circuit breaker, and a timeout per attempt.
- The clients so far are Clerk's Backend API (0028, 0029) and Resend (0037). For each, the attempt timeout (`Timeout`, ten seconds) and the first retry's pause (`RetryDelay`, one second) come from the client's configuration section; the rest are the standard values: at most four attempts and thirty seconds in all.
- The handler retries a request whatever its method, so every request these clients send is safe to repeat: Clerk's reads; Clerk's invitation, which ignores an earlier pending one; and Resend's sends, which carry an idempotency key. A client added later must be checked for this.
- The JWT bearer handler fetches Clerk's signing keys through its own client, outside this policy; it caches them.

## Alternatives considered

- **Polly configured by hand.** Microsoft.Extensions.Http.Resilience builds on it with standard defaults.

## Consequences

- External failures degrade gracefully instead of exhausting the host.
