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

## Alternatives considered

- **Publish after commit without an outbox.** A crash between commit and publish loses the event.
- **Envelope tables in every module's schema.** More tables to operate for the same guarantee.

## Consequences

- Delivery is at least once; handlers must be idempotent.
- The `wolverine` schema is shared infrastructure, not owned by a module.
