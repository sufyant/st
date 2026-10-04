# 0034. Pagination by default

- Status: Proposed
- Date: 2026-10-04

## Context

Unbounded list responses grow with the tenant and hurt latency and memory.

## Decision

- List endpoints are paginated by default.
- Responses carry no unnecessary data.

### How it works

- Pagination is by offset: `?page=1&pageSize=50`. The default page size is 50 and the largest 100; a larger size or a page below 1 is a 400 validation error.
- A page is returned as `{ items, page, pageSize, totalCount }`, the `PagedList<T>` type in SharedKernel. Every module returns the same shape, and its `Map` turns a page of application types into a page of API types (0010).
- The first list endpoint is the tenant list of the admin API (0031).

## Alternatives considered

- **Unbounded lists.** Break as tenants grow.
- **Cursor (keyset) pagination.** Scales to very large lists and is stable under concurrent inserts, but is harder for clients and not needed for the lists the template has. A list that outgrows offsets can switch with its own decision.

## Consequences

- Every list endpoint and client handles paging.
- Counting the total costs a second query per page.
- Offsets get slower deep into a large list, and a row inserted while paging can move items between pages.
