# 0024. Durable outbox and inbox

- Status: Proposed
- Date: 2026-10-04

## Context

Events must not be lost when the business transaction commits, and with many pods a message may arrive more than once.

## Decision

- Events are written in the same transaction as the business data.
- The durable outbox and inbox are enabled deliberately; their envelope tables live in one shared `wolverine` schema.
- Across pods, PostgreSQL row locks with skip-locked reads make each message be taken by one pod; the inbox discards duplicates.
- Messages whose repetition would matter to the business carry a logical deduplication id.

### How it works

- **Storage.** `WolverineFx.Postgresql` keeps the envelopes in the `wolverine` schema, over the application role's direct connection (0019). The migration step creates the schema as the owner (0020). The application starts with `AutoCreate.None` and never changes it.
- **Every local queue is durable.** A message one handler sends to another, in the same module or across modules, is stored in the inbox table before it is handled. If its node dies, another node recovers it.
- **In the tenant transaction.** For a message that carries a tenant, the transaction policy enlists the outbox in the tenant transaction (0016). What the handler sends is stored with its work and leaves only after the commit; an exception or a failed `Result` discards it.
- **Without a tenant.** A message without a tenant has no business transaction. What it sends is stored and sent when it finishes, apart from what it wrote, so such a handler does not do both (0016).
- **No row level security in `wolverine`.** The envelope tables hold every tenant's messages, and Wolverine's own agents read them across tenants over the direct connection, so a policy would have nothing to filter by. Tenant isolation stays in the module tables.
  - Messages therefore carry ids and what a step needs: never a token, a secret or other sensitive data. A handler reads the rest from its module, in its tenant.
  - The one stored message that carries personal data today is `CreateFirstOwnerInvitation`, with the first owner's email address, which nothing else holds until the invitation exists (0026). Its fault, if the step fails, carries it too.
- **The tenant is stored with the envelope** (0017), so a recovered message runs under its tenant.
- **Failures.** A message that still fails after its retries goes to the dead letter table. Wolverine then publishes a `Fault<T>` for it, carrying the original message and the exception's type only (0022). Publishing the fault is best-effort and not part of the dead letter move.
- **Integration events** are records in the publishing module's `*.Contracts` (0009), published through the outbox. The first is `TenantActivated` (0026).
- **Duplicates.** A message is marked handled after its transaction commits, so a crash in between delivers it again. Phase 5's handlers are idempotent through their own state, so they need no deduplication id: an invitation issues its token once, and a tenant leaves provisioning once.

## Alternatives considered

- **Publish after commit without an outbox.** A crash between commit and publish loses the event.
- **Envelope tables in every module's schema.** More tables to operate for the same guarantee.

## Consequences

- Delivery is at least once; handlers must be idempotent.
- The `wolverine` schema is shared infrastructure, not owned by a module.
- A request that sends a message answers before the message is handled; clients and tests wait for the outcome (0042).

## Verified

- Phase 5, against WolverineFx 6.45.0: Wolverine's `DatabaseEnvelopeTransaction` writes into the transaction it is given. A message published by a tenant command is visible inside the command's transaction and not outside it until the commit. It is handled under the command's tenant once the command commits, and it is never sent when the command throws or returns a failed `Result`.
