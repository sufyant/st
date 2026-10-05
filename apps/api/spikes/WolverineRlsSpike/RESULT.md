# Result: Wolverine owns the transaction, PostgreSQL RLS still holds — **yes**

Run: `cd apps/api/spikes/WolverineRlsSpike && dotnet run` (Docker needed; Testcontainers `postgres:18`).
WolverineFx / .Postgresql / .EntityFrameworkCore / .RuntimeCompilation 6.45.0, Npgsql EF Core 10.0.3, .NET 10.
Final run: 11/11 green, three runs in a row.

## Report

| #   | Test                                                                 | Result |
|-----|----------------------------------------------------------------------|--------|
| 1   | Saga start writes note + saga row, both carry tenant A               | pass   |
| 2   | Inside the handler, tenant B's seeded note is not visible            | pass   |
| 3   | Cascade over durable local queue: runs under A, saga loaded, `Version` 0→1 | pass |
| 4   | Write + saga change + cascade, then throw: no note, no saga change, no envelope | pass |
| 5   | `Maximum Pool Size=1`: after a tenant A message the pooled connection has no `app.tenant_id` | pass |
| 6   | No tenant: reads 0 rows, insert fails with `42501` (RLS `WITH CHECK`) | pass  |
| 7   | Tenant A's saga id sent under tenant B: saga not loaded, `NotFound` runs | pass |
| 8   | Two `Separated` handlers, own DbContext + schema: one throws, the other commits | pass |
| 9   | Two concurrent updates of one saga: one hits the version check, is retried once, no lost update (`Steps`=2, `Version`=2) | pass |
| 10a | Handler returns failed `Result` after a change: **the change commits** | pass (records behaviour) |
| 10b | Stock way to stop it: `Validate` returning `HandlerContinuation.Stop` → nothing written | pass |

Red phase (interceptor off, `SPIKE_NO_INTERCEPTOR=1`): 1–5 and 7–10 failed with
`42501: new row violates row-level security policy for table "notes"` / `"spike_sagas"`, 8 failed on the missing
audit row. 6 passed (it expects the failure).

## Mechanism: A

`TenantTransactionInterceptor` (an EF Core `DbTransactionInterceptor`) runs
`SELECT set_config('app.tenant_id', @tenant, true)` in `TransactionStartedAsync`. It reads the tenant from the
DbContext instance (`ITenantAware.CurrentTenantId`), which reads `IMessageContext.TenantId` lazily.

Real order, from Wolverine's generated code (`generated/*.cs`, written by `SpikeFixture` after a run):

1. `new SpikeDbContext(_options, context)` — Wolverine **inlines** the constructor and passes the handler's own
   `MessageContext` (no service location, no second instance; `AlwaysUseServiceLocationFor` not needed).
2. `EfCoreEnvelopeTransaction` + `EnlistInOutboxAsync` (no query).
3. `if (CurrentTransaction == null) BeginTransactionAsync` → interceptor → `set_config`.
4. `try {` saga id → `FindAsync<SpikeSaga>` → handler → version bump → `SaveChangesAsync`
   (wrapped into `SagaConcurrencyException`) → `SaveChangesAsync` → commit → outbox flush `} catch { Rollback; throw; }`.

B and C were not tried because A works. From the source (`SagaChain.DetermineFrames`: user middleware is applied,
then `ApplyTransactionSupport` inserts at index 0), a `Before` middleware would land after step 3 and before the saga
load — not verified by a test.

## Forced-public types

| Type | Why |
|------|-----|
| Saga (`SpikeSaga`) and handler classes | Wolverine discovery excludes non-public types (`HandlerDiscovery`: "Is not a public type"; `IncludeType` throws). All-internal run: `IndeterminateRoutesException` for every message. |
| Messages | Parameter types of public handler methods (C# `CS0051`). |
| DbContext | Same `CS0051`; and on its own: internal DbContext used only via saga persistence → `Compilation failures! CS0122: 'SpikeDbContext' is inaccessible due to its protection level` (Wolverine compiles handlers into a separate assembly). |
| Entities (`Note`), `ITenantAware`, interceptor | Not forced by Wolverine; public only because the public DbContext exposes them. |

## Settings / additions not in the spec

- `WolverineFx.RuntimeCompilation` package: without it host start fails ("TypeLoadMode.Dynamic … no IAssemblyGenerator (Roslyn) is registered").
- `Version` mapped with `.IsConcurrencyToken()`: Wolverine bumps `Version` for EF Core sagas but does not configure the token; without it the version check never reaches the `WHERE` clause.
- `opts.OnException<SagaConcurrencyException>().RetryTimes(3)`: stock Wolverine does not retry a saga version conflict (test 9 failed with `SagaConcurrencyException` before it).
- Interceptor must ignore Wolverine's `"*DEFAULT*"` tenant (`StorageConstants.DefaultTenantId`): an untenanted message has that, not null. Before the fix test 6 failed with `22P02: invalid input syntax for type uuid: "*DEFAULT*"`.
- `opts.ApplicationAssembly = typeof(SpikeHost).Assembly` (handlers live in the test exe).
- DB: schema `wolverine` created by the superuser `AUTHORIZATION app_user` (Wolverine creates its envelope tables as `app_user`); `GRANT USAGE` on `app`/`audit`, `GRANT SELECT, INSERT, UPDATE, DELETE` on the three RLS tables.
- Test 10a: the failed `Result` is returned to the caller by `InvokeForTenantAsync<Result>`; Wolverine does not look at it.
- Spike-local `Directory.Packages.props` (adds `WolverineFx.EntityFrameworkCore` 6.45.0 on top of the central file) and `.editorconfig` (CA1707 off, as for `tests/**`). `SharedKernel` is referenced for `Result`, not changed.
