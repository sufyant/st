# 0041. Resilience on outbound calls

- Status: Proposed
- Date: 2026-10-04

## Context

Every external call can fail or hang (Nygard, *Release It!*).

## Decision

- Outbound HTTP clients use timeouts, retries and circuit breakers through Microsoft.Extensions.Http.Resilience.

## Alternatives considered

- **Polly configured by hand.** Microsoft.Extensions.Http.Resilience builds on it with standard defaults.

## Consequences

- External failures degrade gracefully instead of exhausting the host.
