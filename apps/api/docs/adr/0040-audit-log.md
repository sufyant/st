# 0040. Audit log as business data through the outbox

- Status: Proposed
- Date: 2026-10-04

## Context

An audit trail is business data kept for years, unlike operational logs (Fowler's Audit Log pattern; OWASP logging guidance). Records must not be lost and must not break module boundaries.

## Decision

- The audit log is separate from the regular log and lives in the Audit module: a single table with a tenant column, protected by RLS.
- The module that runs a command writes the audit event to the outbox in its own transaction; the Audit module consumes and stores it.
- Recorded: successful state-changing commands, denied authorization attempts and system admin entries into a tenant.
- Content: who, when, which tenant, which operation, which record, and before and after values where needed.
- Queries are not audited.
- If volume grows, the table is partitioned by date.

## Alternatives considered

- **Audit in the regular log.** Logs are rotated and are not business data.
- **Each module writes directly to the audit table.** Crosses module boundaries (0008).

## Consequences

- Audit records are not lost with the business transaction and do not cross module boundaries.
- A denied attempt has no successful business transaction to write its event in; how that event is recorded must be settled before Phase 6.
