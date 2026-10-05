# Spike: can Wolverine own the transaction while PostgreSQL RLS still holds?

## Goal
A throwaway trial that answers one yes/no question with tests. It does not change production code.

Question: if Wolverine's EF Core transactional middleware opens and commits the transaction, and Wolverine's own saga
storage (EF Core) is used, do tenant isolation (RLS) and atomic commit still hold?

## Rules
- Do NOT read or follow `docs/adr/`, `docs/ARCHITECTURE.md` or `docs/KICKOFF_PROMPT.md`. They are not authoritative.
- Do NOT reuse `src/Shared/Tenancy/TenantTransaction*`, `src/Api/Messaging/*` or any existing handler. The trial must
  show what stock Wolverine does.
- Put everything in `apps/api/spikes/WolverineRlsSpike/` (one test project, not added to `Api.slnx`). Change nothing else.
- Same versions as the repo: .NET 10, WolverineFx 6.45.0, WolverineFx.Postgresql, WolverineFx.EntityFrameworkCore,
  Npgsql EF Core, xunit.v3, Shouldly, Testcontainers `postgres:18`.
- Write each test first. Run it. See it fail for the right reason. Then make it pass.
- If a test cannot pass with stock Wolverine, STOP on that test and report it. Do not work around it with a custom
  transaction owner. A "no" is a valid result.

## Setup
Database (created by the superuser in the fixture):
- Role `app_user`: LOGIN, NOSUPERUSER, NOBYPASSRLS, owns no table in schema `app`.
- Schema `app`, owned by the superuser, with `notes(id uuid pk, text text, tenant_id uuid not null default
  NULLIF(current_setting('app.tenant_id', true), '')::uuid)` and the saga table (same `tenant_id` column and default).
- On both tables: `ENABLE` and `FORCE ROW LEVEL SECURITY`, one policy
  `USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid) WITH CHECK (same)`.
- The application connects as `app_user` only. Wolverine's envelope tables live in schema `wolverine`.

Wolverine:
- `PersistMessagesWithPostgresql(..., "wolverine")`, `UseEntityFrameworkCoreTransactions()` (Eager mode),
  `Policies.AutoApplyTransactions()`, `Policies.UseDurableLocalQueues()`,
  `MultipleHandlerBehavior = MultipleHandlerBehavior.Separated`.
- DbContext registered with `AddDbContextWithWolverineIntegration<T>((sp, o) => ...)`.
- A saga class that inherits `Wolverine.Saga`, has `Id` and `int Version`, and is mapped in the DbContext.

Declaring the tenant. The hard part is ORDER: the tenant must be declared before the first query of the
transaction, and the saga row is loaded early. Wolverine inserts its own transaction frame at index 0 of
`chain.Middleware` (`EFCorePersistenceFrameProvider.ApplyTransactionSupport`), so do not assume a user middleware
runs before it. Read the generated code (`dotnet run -- codegen preview` or `opts.CodeGeneration`) and report the
real order. Try in this order and report which one works:
- A. An EF Core `DbTransactionInterceptor` that runs `SELECT set_config('app.tenant_id', @tenant, true)` in
  `TransactionStarted/TransactionStartedAsync`. It fires because Wolverine 6.45.0 generates
  `if (db.Database.CurrentTransaction == null) await db.Database.BeginTransactionAsync(ct);`
  (`EnrollDbContextInTransaction.cs`). The open question is where the interceptor gets the tenant from at that
  moment. Candidates: a scoped holder filled from `Envelope.TenantId` (check that Wolverine does not inline a second
  instance; `opts.CodeGeneration.AlwaysUseServiceLocationFor<T>()` exists), or the DbContext instance itself.
- B. A Wolverine middleware `Before(Envelope, TDbContext, CancellationToken)` that begins the transaction if none is
  open and runs `set_config`. Wolverine reuses an open transaction (`CurrentTransaction == null` check above).
- C. Wolverine's own shared-database tenancy (`AddDbContextWithWolverineManagedConjoinedTenancy<T>`), which pins the
  tenant to the DbContext before the transaction starts (`ConjoinedTenancy.TenantIdOf(context)`), plus interceptor A
  reading that value. Use C only if A and B fail, and list everything C adds (tables, filters, interfaces).

## Tests (one behaviour each)
1. A saga started with `InvokeForTenantAsync(tenantA, ...)` writes a note and the saga row. Both rows carry tenant A.
   (Proves the setting was active inside Wolverine's transaction.)
2. Inside that handler, a note of tenant B that was seeded beforehand is not visible.
3. The saga continues through a cascaded message on a durable local queue (no HTTP). The second handler runs under
   tenant A, loads the saga under RLS, and `Version` goes up.
4. A handler writes a note, changes the saga, cascades a message and then throws. Nothing is stored: no note, no saga
   change, no envelope for the cascaded message.
5. Connection pool with `Maximum Pool Size=1`. After a tenant A message, a tenant-less query on the same data source
   returns NULL or '' for `current_setting('app.tenant_id', true)`.
6. A message sent without a tenant cannot insert into `app.notes` and reads zero rows.
7. A message for tenant A's saga id sent under tenant B does not load the saga (`NotFound` path or equivalent).
8. Two handlers for the same event, each with its own DbContext and schema. One throws. The other one's work is
   committed. (Each listener has its own transaction.)
9. Two messages for the same saga processed at the same time: one hits the version check and is retried. No lost update.
10. Our handlers return a `Result`. A handler returns a failed `Result` after it changed data. Record what happens
    (commit or not). If it commits, show the smallest stock way to stop it (for example a `Validate`/`Before` method)
    and test that way.

## Also record (no test needed)
- Which types had to be `public` (saga, messages, handlers, DbContext). Try the DbContext as `internal` once.
- Any setting you had to add that is not listed above.

## Report
A table: test number, pass or fail, and for a fail the exact error and the file and line. Then: mechanism A or B.
Then the list of forced-public types. Keep the spike folder so the result can be reproduced.
