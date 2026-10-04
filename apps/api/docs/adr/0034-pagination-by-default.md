# 0034. Pagination by default

- Status: Proposed
- Date: 2026-10-04

## Context

Unbounded list responses grow with the tenant and hurt latency and memory.

## Decision

- List endpoints are paginated by default.
- Responses carry no unnecessary data.

## Alternatives considered

The overview records none; the obvious one is unbounded lists, which break as tenants grow.

## Consequences

- Every list endpoint and client handles paging.
