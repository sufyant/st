# 0040. Audit log as business data through the outbox

- Status: Proposed
- Date: 2026-10-04

## Context

An audit trail is business data kept for years, unlike operational logs (Fowler's Audit Log pattern; OWASP logging guidance). Records must not be lost and must not break module boundaries.

## Decision

- The audit log is separate from the regular log and lives in the Audit module: a single table with a tenant column, protected by RLS.
- A command's audit event is written to the outbox in the command's own transaction, by the host's pipeline for the commands a module marks (see How it works); the Audit module consumes and stores it.
- Recorded: successful state-changing commands, denied authorization attempts and system admin entries into a tenant.
- Content: who, when, which tenant, which operation, which record, and before and after values where needed.
- A denied authorization attempt has no business transaction of its own. The pipeline writes its audit event to the outbox in a separate, small transaction.
- If the tenant context could not be resolved (for example, the user is not a member of the tenant), the denial is written to the security log instead, because the audit table is tenant-scoped.
- Queries are not audited.
- If volume grows, the table is partitioned by date.

### How it works

- **The table.** `audit.entries (id, tenant_id, occurred_at, actor_id, kind, operation, details)`, a tenant entity under row level security (0014). `kind` is `Command`, `Denied` or `SystemAdminEntry`; `details` is JSON. The application role may only insert and read entries, never change or delete them.
- **The message.** Every record travels as `RecordAuditEntry`, a message of `Audit.Contracts`, in the tenant where the event happened. The Audit module stores it under that tenant. Its sender chooses the entry's id, so a message that arrives twice is stored once (0024).
- **The host records, on behalf of the modules.** The host may reference only the modules' `X.Api` projects (0006), so `Audit.Api` offers it a small facade, `AuditTrail`, which publishes the message on the bus it is given.
- **Successful commands.** A module marks a state-changing command with `IAuditedCommand`, from SharedKernel. The command names its actor and what the record keeps of it (`AuditDetails`), never a credential such as an invitation token.
  - When a marked command succeeds in a tenant, the host's transaction policy (0016) publishes its record just before the commit, in the command's own transaction. It is kept or lost with the command's work, and a failed `Result` or an exception leaves no record.
  - The record holds the command's name as the operation, and as details the command's `AuditDetails` and the value of its `Result<T>`, which names the record a command created.
  - Marked so far: ControlPlane's role, member and invitation commands, and accepting an invitation; the Notifications module's scheduling commands (0037). The onboarding saga's steps, deliveries and system jobs are not marked.
- **Denials.** Authorization's result handler in the host sees every request it refuses (0030). For a signed-in user whose request entered a tenant, a member without the endpoint's permission or a system admin inside a tenant, it publishes a `Denied` record on the request's bus, which carries the tenant (0015). The durable local queue stores it right away in a small transaction of its own. Any other denial, such as a non-member's request answered 404, is an `AuthorizationDenied` security event in the `Api.Security` log category (0039).
- **System admin entries** are published the same way by the admin tenant group's endpoint filter (0031).

## Alternatives considered

- **Audit in the regular log.** Logs are rotated and are not business data.
- **Each module writes directly to the audit table.** Crosses module boundaries (0008).

## Consequences

- Audit records are not lost with the business transaction and do not cross module boundaries.
- Denials before tenant resolution are only in the security log (0039), not in the audit table.
- Before Phase 6 a system admin's entry into a tenant was recorded only as a security event in the log (0031); now it is recorded in both.
- Before and after values are not recorded yet; the result of a command that creates a record stands in for the after value. A command whose audit needs them adds them to its `AuditDetails` when that need appears.
- The marker puts audit vocabulary into SharedKernel, which otherwise holds no business rules (0004); it is a single interface, so every module can mark its commands without depending on the Audit module.
