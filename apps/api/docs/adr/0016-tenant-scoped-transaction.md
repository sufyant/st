# 0016. One transaction per tenant-scoped message

- Status: Proposed
- Date: 2026-10-04

## Context

RLS filters by a tenant value set on the database connection. Requests use pooled connections (0019), so any value set outside a transaction can leak to another request on the same connection.

Wolverine handles each message in a service scope of its own, separate from the HTTP request's scope. A connection or transaction opened for the request is therefore not the one a command handler sees.

## Decision

- **The transaction belongs to the message.** Every message that carries a tenant runs inside a single transaction, whether it comes from a request, a cascade or a queue. The active tenant is set at the start of that transaction, local to it (`set_config('app.tenant_id', ..., true)`). RLS filters by that value. Reads follow the same rule.
- **A request reaches its tenant through a message.** Tenant resolution (0015) puts the tenant on the request's message bus, and Wolverine carries it in the envelope's tenant id. A tenant-scoped endpoint reaches its module through one invocation (`InvokeAsync`), so the request's transaction is that command's transaction.
- **The host opens the transaction.** A Wolverine handler policy in the host (`TenantTransactionPolicy`) wraps every handler:
  - Before the handler, it begins the transaction when the envelope carries a tenant. A tenant id that is not a GUID fails the message.
  - After the handler, it commits only if the handler succeeded. An exception, or a returned `Result` that reports a failure, leaves the transaction uncommitted, and disposing the message's scope rolls it back. An expected failure undoes the handler's work just as an exception does (0032).
  - The policy passes the handler's return value to the commit explicitly. Middleware cannot do this, because Wolverine binds middleware parameters by exact type, and handlers return `Result<T>` as often as `Result`.
- **One connection per scope.** `TenantTransaction` (0048) holds the scope's single connection. Every module DbContext in the scope runs on it. When a save would begin a transaction of its own, an interceptor makes it join the tenant transaction instead.
- **Messages without a tenant** run without a tenant transaction, for example catalog work (0021). Tenant data stays invisible to them.
- **Wolverine resolves DbContexts from the message's scope.** Module DbContexts are built by factory registrations on the scope's connection, which Wolverine can only resolve from the scope, so the host allows service location (`ServiceLocationPolicy.AlwaysAllowed`). Tests prove that a handler's DbContext and the transaction middleware share one connection.

## Alternatives considered

- **Set the tenant per session when a connection is opened.** Leaks between requests that share a pooled connection.
- **Transactions only for writes.** Reads would run without a tenant and RLS would have nothing to filter by.
- **One transaction per HTTP request, shared with handlers through ambient state (`AsyncLocal`).** Would keep several commands of one request in one transaction, at the price of hidden state that flows implicitly.
- **Separate request and command transactions.** A request could span several transactions, and its own transaction would serve only endpoints that touch the database directly.
- **Commit regardless of a failed Result.** Relies on every handler returning failures before it writes anything.

## Consequences

- Reads also pay for a transaction.
- The transaction is opened by the host pipeline (0022), not by handlers.
- An endpoint that sends two commands gets two transactions; tenant endpoints therefore send one.
- The durable outbox (0024) has to write its envelopes in this transaction; Phase 5 decides whether Wolverine's EF Core transaction support replaces or joins it.
