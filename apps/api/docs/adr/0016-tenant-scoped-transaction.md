# 0016. One transaction per tenant-scoped request and message

- Status: Proposed
- Date: 2026-10-04

## Context

RLS filters by a tenant value set on the database connection. Requests use pooled connections (0019), so any value set outside a transaction can leak to another request on the same connection.

## Decision

- Every tenant-scoped request and every tenant-scoped message runs inside a single transaction.
- The active tenant is set on the connection at the start of that transaction; RLS filters by that value.
- Reads follow the same rule.

## Alternatives considered

- **Set the tenant per session when a connection is opened.** Leaks between requests that share a pooled connection.
- **Transactions only for writes.** Reads would run without a tenant and RLS would have nothing to filter by.

## Consequences

- Reads also pay for a transaction.
- The transaction is opened by the host pipeline (0022), not by handlers.
