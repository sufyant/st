# 0035. Per-tenant in-memory rate limiting

- Status: Proposed
- Date: 2026-10-04

## Context

One tenant must not be able to exhaust the API for others. A distributed limiter needs Redis, which the template does not require at the start.

## Decision

- Rate limits are per tenant, in memory in each pod.
- Requests without a tenant are partitioned too: an authenticated request outside a tenant by user id, an unauthenticated request by IP address.
- Limits come from configuration and are not tied to a plan.
- The effective limit is multiplied by the number of pods; this is accepted for the start.
- A global limit comes with Redis when scaling requires it.
- Implemented with ASP.NET Core's rate limiter as a fixed window per partition, applied to the version group: `RateLimiting:PermitLimit` requests per `RateLimiting:Window`, no queueing. A rejected request gets 429 Problem Details with a `Retry-After` header.
- Until tenant resolution exists (0015), the tenant partition is keyed by the slug in the route. From then on, the tenant bucket is used only for requests whose membership has been verified, keyed by the resolved tenant id, and the limiter runs after authentication and tenant resolution. A non-member or unauthenticated request is limited by user id or IP address. The slug in the route is never trusted as a key: a random slug would escape the limit, and someone else's slug would spend that tenant's limit.
- The client address comes from `X-Forwarded-For` only when the sender is a trusted proxy. Trusted proxies (`ForwardedHeaders:KnownProxies`, IP addresses) and networks (`ForwardedHeaders:KnownNetworks`, CIDR) come from configuration. Both are empty by default, and then forwarded headers are not processed at all, because ASP.NET Core's middleware would believe them from any sender when given no list. They are processed before request logging and rate limiting.

## Alternatives considered

- **Global limit in Redis now.** Exact, but adds infrastructure before it is needed.
- **Plan-based limits.** Billing is not part of the template (0004).

## Consequences

- Limits are approximate across pods.
- Clients behind a shared IP address share the limit for unauthenticated requests.
- Behind a reverse proxy, the deployment must list the proxy or its network; otherwise every unauthenticated client shares the proxy's address and its limit.
