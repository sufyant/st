# 0035. Per-tenant in-memory rate limiting

- Status: Proposed
- Date: 2026-10-04

## Context

One tenant must not be able to exhaust the API for others. A distributed limiter needs Redis, which the template does not require at the start.

## Decision

- Rate limits are per tenant, in memory in each pod.
- Limits come from configuration and are not tied to a plan.
- The effective limit is multiplied by the number of pods; this is accepted for the start.
- A global limit comes with Redis when scaling requires it.

## Alternatives considered

- **Global limit in Redis now.** Exact, but adds infrastructure before it is needed.
- **Plan-based limits.** Billing is not part of the template (0004).

## Consequences

- Limits are approximate across pods.
