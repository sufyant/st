# 0010. Separate API, application and contract types

- Status: Proposed
- Date: 2026-10-04

## Context

One type used for the HTTP shape, the internal command and the cross-module contract couples three audiences: changing it for one breaks the others.

## Decision

Three type layers are kept separate and are not converted into each other implicitly:

| Layer | Example | For | Where |
| --- | --- | --- | --- |
| API types | `InviteMemberRequest`, `TenantResponse` | API clients; reflected in OpenAPI | `X.Api` |
| Application types | `InviteMemberCommand` | Orchestration inside the module | `X.Application` |
| Module contract | `TenantSummary`, `IControlPlaneModule`, integration events | Other modules | `X.Contracts` |

## Alternatives considered

- **One shared type across layers.** Less code; any change ripples to clients and other modules.

## Consequences

- More types and hand-written mapping (no mapping library, 0002).
- Separate response types also close property-level authorization gaps (0030).
