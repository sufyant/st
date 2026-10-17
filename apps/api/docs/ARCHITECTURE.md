# Architecture: multi-tenant SaaS starter template (API)

Last updated: 2026-10-17

This document is the single source of truth for the API in `apps/api/`. If the code does not agree with this document, the code changes. No other file overrides it.

Each rule has an id (for example `R4`, `W2`). Use the id when you refer to a rule in a review, a commit message or a comment.

## Purpose and scope

The purpose is a multi-tenant SaaS starter template. Every future product is built on it. The scope is the API only. Correctness, clear boundaries and tenant isolation are more important than speed.

### Fixed rules

1. Each decision agrees with the books and with industry practice.
2. Each deviation from the books is written down openly. A silent deviation is not permitted.
3. There is no billing.
4. There are no commercially licensed packages: MediatR, AutoMapper, MassTransit v9+, FluentAssertions.
5. The architecture controls the tool. The tool does not control the architecture. If a tool does not fit the structure, the tool goes or the deviation is approved openly.
6. The template is platform independent. Provider detail (example: the database provider) does not go into the architecture. It goes into the setup notes.
7. Infrastructure without a use does not go in. A part is added only if it catches a failure that no other control catches.

Setup notes for providers are in docs/setup.md.

### Scope of the first template

The first template runs one flow from start to end: tenant creation.

| In scope | Reason |
| --- | --- |
| `POST /v1/admin/tenants` | The work itself |
| The first system admin comes from configuration | The first person who creates a tenant cannot get on the staff list in another way |
| Tenant creation process (saga, three steps) | The approved flow |
| One handler that sends the owner invitation email (Notifications) | Step three of the process. The member invitation email has a handler of its own (section 6, "Member invitation") |
| The handlers that write the audit record of every command, in the tenant's log and the platform's log (Audit, K3) | The uses of the Audit module |
| The endpoint that accepts an invitation | Without it, the first Owner cannot exist |
| `GET /v1/me/tenants` | The user sees their tenants after sign-in |
| `GET /v1/me`, `GET /v1/tenants/{tenantId}` and `GET /v1/admin/tenants/{tenantId}` | The web and the admin app read the signed-in user and one tenant (catalog, step 20c) |
| Each user's language and time zone, `PATCH /v1/me/preferences`, and the two invitation emails in English, Turkish and Arabic (catalog, K8, step 20j; section 6, "Language and time zone") | The web speaks three languages, and its Preferences screen sets both values for every tenant and device. The API writes text only in emails |
| Each user's email address and names in the catalog, which only a system admin changes, and `PATCH` and `DELETE /v1/admin/users/{userId}` (A7, A8) | The admin screens of step 19d list users by email address and name, not by the identity provider's id. The identity provider keeps no name |
| The identity provider's invitation for an invited address without an account, and its withdrawal (A7; section 6, "Invitation sign-up") | The identity provider is invite-only. Without its invitation, a person with no account, the first Owner of a new tenant included, cannot sign up |
| One read endpoint behind the tenant door. Example: `GET /v1/tenants/{tenantId}/members` | It exercises the tenant door, membership, RLS and the permission check |
| `GET /v1/admin/tenants` and `GET /v1/admin/users` | The admin app lists every tenant and every user |
| `POST /v1/admin/tenants/{tenantId}/suspend` and `.../reactivate` | A system admin stops a tenant's members, for a reason, and lets them in again (section 6) |
| `PATCH /v1/admin/tenants/{tenantId}`, `POST .../close` and `.../reopen` | Only the platform renames a tenant; a system admin closes a tenant and reopens it within the reopen period (catalog, K4, step 20h; section 6, "Tenant lifecycle") |
| `POST /v1/admin/users/{userId}/suspend` and `.../reactivate` | A system admin stops a user in every tenant, and lets them in again (A9) |
| `POST`, `GET /v1/tenants/{tenantId}/invitations` and `DELETE .../invitations/{invitationId}` | An Owner or an Admin invites members, lists the pending invitations and revokes one (catalog, step 20d; section 6, "Member invitation") |
| `PATCH` and `DELETE /v1/tenants/{tenantId}/members/{userId}` | An Owner or an Admin changes a member's role and removes a member (catalog, step 20e; section 6, "Member management") |
| `GET /v1/tenants/{tenantId}/audit-log` and `GET /v1/admin/audit-log` | An Owner or an Admin reads the tenant's audit log ("Activity"); a system admin reads the platform's (catalog, step 20f; section 6, "Reading the audit logs") |

The catalog of every endpoint of the core API is `docs/api-catalog.md`: who may call it, the audit record it writes, the notification it sends, the external system it reaches and the step that builds it. A change to an endpoint changes the catalog in the same pull request.

Out of scope: custom roles and their assignment, tenant deletion, support access, in-app notifications, adding a second system admin.

## 1. The big picture

The system is one code base and one build output. This output runs as two process types: web and worker. A third role, all, runs both in one process, for local development and tests.

| Decision | Source |
| --- | --- |
| The system is one deployment unit. The code is divided by business area | Richards and Ford, FSA 2nd ed., Chapter 11 |
| Each module is a bounded context | Evans, Chapter 14. Khononov, Chapter 3 |
| One artifact, two process types. Web serves HTTP requests. Worker runs background work. A setting selects the role | Twelve-Factor, Factor VIII. Nygard, Chapter 5 (Bulkheads) |
| Each module that stores data has its own PostgreSQL schema. No foreign keys and no joins across modules | Evans, Chapter 14. Richardson, "Database per Service" (adapted to a monolith) |
| The host contains no business rule. It only connects the modules | Industry practice (composition root) |

In Kubernetes this is two Deployments and one image. The requirement is a separate process, not a separate machine.

## 2. Module boundary and structure

Each module is five projects. Another module sees only the `Contracts` project.

| Project | Content | Depends on |
| --- | --- | --- |
| `X.Contracts` | Interfaces, DTOs, event types. No domain types | `SharedKernel` |
| `X.Api` | HTTP endpoints | Application, Contracts, `SharedKernel` |
| `X.Application` | Use case handlers, sagas | Domain, Contracts, other modules' Contracts, `SharedKernel` |
| `X.Domain` | Business rules | `SharedKernel` |
| `X.Infrastructure` | EF Core, adapters for external services, contract implementations | Application, Domain, Contracts, `SharedKernel`, `Tenancy` |

`SharedKernel` holds the result and error types, paging and the list contract (`ListRequest`, `ListSort`, `ListFilter`, K5 in the catalog), the cursor of the audit logs (`CursorRequest`, `CursorPage`, `Cursor`), the marker of a tenant entity, one kind of endpoint metadata (`IdempotencyKeyRequired`), the one list of languages (`Language`, K8 in the catalog) and the converter that reads an enum by its exact JSON name (`ExactNameEnumConverter`); `Tenancy` holds the base of a module DbContext, the tenant declaration at the start of each transaction, row level security in migrations, the names of the database roles and the settings that every PostgreSQL connection of the API gets. The error types are validation, not found, conflict, forbidden, unprocessable and too many requests; `Api/ErrorHandling/Problems.cs` maps them to 400, 404, 409, 403, 422 and 429 (RFC 6585). `SharedKernel` also holds the type of an error code, `ErrorCode`, the codes of paging (`PagingErrorCodes`) and the field codes of a validation answer, `FieldErrorCode` (catalog, K5). It holds no module's codes: each owner declares its own in one static class whose name ends with `ErrorCodes`, the host in `Api/ErrorHandling/HostErrorCodes.cs` and each module in its Domain project, such as `ControlPlane.Domain/ControlPlaneErrorCodes.cs`, so every layer of the module reaches them. The OpenAPI document joins them into one enum.

- Two controls enforce the boundary: project references (compilation) and architecture tests. Source: Ford, Parsons, Kua, Chapter 2.
- The `Api` project cannot reach the `DbContext`. Source: Evans, Chapter 4.
- A read across modules is a plain method call. The interface is in `A.Contracts`. Its implementation is an `internal sealed` class inside A. The exception is an implementation that another module's handler takes: it is public and sealed, so that Wolverine builds it on its module's DbContext (W1, W9). Today that is `UserNames` only. Source: Gamma et al., Chapter 4 (Facade). Evans, Chapter 14 (Open Host Service).
- The shared kernel stays small and carries no business concept. Source: Evans, Chapter 14.
- `ExactNameEnumConverter` is in `SharedKernel` because `Language` (in `SharedKernel`) and `MemberRole` (in `ControlPlane.Api`) both use it. An enum names its converter in an attribute, so the converter must be in the enum's project or in a project it references. `SharedKernel` references no project of ours, and every project references it, so it is the one place that both enums reach. The converter is no business rule: it reads an enum by the exact JSON name of one member, with the same lookup as `ListFilter`. Source: Evans, Chapter 14 (Shared Kernel). Hunt and Thomas, *The Pragmatic Programmer*, Topic 9 (DRY).
- `Language` is in `SharedKernel` because three layers need the one list: ControlPlane's Domain (a user and an invitation keep one), its Api (the requests and responses) and its Contracts (the messages that Notifications reads to write an email). A Domain project references only `SharedKernel` (`ProjectReferenceTests`), so `SharedKernel` is the one project they all see. A language is no business rule of a module: it is a closed list of values with no behaviour, which no single module owns, as the field codes of a validation answer (`FieldErrorCode`) are. A copy in each layer would make a new language a change in three places, and a copy can drift. Source: Evans, Chapter 14 (Shared Kernel: a small, explicit part of the model that the teams agree to share).
- Test projects are next to their module. Tests of the host and system-wide tests (architecture, end to end) stay in the top tests folder (N9).
- Names, folders, routes and permissions follow section 10.

## 3. Messaging

If a module needs data, it reads through a contract. To tell other modules that something happened, it uses an event. The outbox carries the event.

### Outbox rules

| # | Rule | Source |
| --- | --- | --- |
| O1 | The event record is written in the same transaction as the business data | Richardson, Chapter 3. Kleppmann, Chapter 11 |
| O2 | Delivery happens after the commit and separately | Richardson, Chapter 3 |
| O3 | Each listening module runs in its own transaction | Vernon, Chapter 10 |
| O4 | Delivery is at least once. A message that arrives again has no second effect | Hohpe and Woolf, Chapter 10 (Idempotent Receiver) |
| O5 | There is one outbox. It is in a shared infrastructure schema | Industry practice. See the deviations list |

### Requirements for the messaging tool

The tool is Wolverine. The tested version is 6.45.0. The tool is measured against these nine requirements. Each requirement is tested with real PostgreSQL.

1. The event record is written in the same transaction as the business data.
2. Each listening module runs in its own transaction.
3. With many pods, only one pod takes a message at a time.
4. A message that arrives again has no second effect.
5. If a pod dies, its unfinished message is not lost. Another pod takes it.
6. A message that always fails is moved aside. The queue does not block.
7. The tenant travels with the message.
8. The `Domain` and `Contracts` projects do not see the tool's types. `Api`, `Application` and `Infrastructure` can.
9. The architecture controls the tool. We decide the module structure, the schema layout and the transaction rule.

Requirements 3 and 5 are tested with two workers on one database (`TwoWorkerTests`).

### Handlers and pipeline

Wolverine is the dispatcher. An endpoint sends a command or a query through `IMessageBus`. A handler is a plain class. Shared behaviour is Wolverine middleware. Source: Fowler, PoEAA Chapter 9 (Service Layer). Hohpe and Woolf, Chapter 3 (Pipes and Filters) and Chapter 10 (Message Dispatcher).

| Order | Command chain | Query chain |
| --- | --- | --- |
| 1 | HTTP: authentication, rate limit, the user's state (A9) and tenant membership, authorization (ASP.NET Core) | HTTP: authentication, rate limit, the user's state (A9) and tenant membership, authorization (ASP.NET Core) |
| 2 | Logging and tracing | Logging and tracing |
| 3 | The transaction opens, the tenant is declared | The transaction opens, the tenant is declared |
| 4 | Input validation | Input validation |
| 5 | Handler | Cache (only on marked queries) |
| 6 | Save, commit, then outbox dispatch | Handler |

Authorization runs once, in ASP.NET Core, before the endpoint sends the message. A refused request opens no transaction. A message from a queue has no caller and is not authorized again.

- Wolverine opens and closes the transaction. We set the rule: each handler runs in its own transaction.
- A handler that calls an external service uses no DbContext, so no transaction is open during the call. It returns its result as a message, or nothing. A step that returns nothing is heard by the saga only when it fails, through its fault. Source: Nygard, Chapter 5 (Integration Points).
- A domain event is handled inside the module and in the same transaction. An integration event leaves the module through the outbox. Source: Vernon, Chapter 8.
- HTTP endpoints are plain ASP.NET minimal APIs. The tool's own endpoint model (Wolverine.Http) is not used.

### Wolverine setup rules

Rules W1 to W9 come from a spike. The spike is in `apps/api/spikes/WolverineRlsSpike`. The T numbers are the tests in the spike.

| # | Rule | Reason | Evidence |
| --- | --- | --- | --- |
| W1 | Wolverine's EF Core middleware opens and closes the transaction. There is no hand-written transaction code. One exception: TenantDirectory opens a short transaction of its own in the HTTP pipeline, before any message is sent, to read the membership. | The saga record, the business data and the outgoing message go into one transaction (O1, S6) | T3, T4 |
| W2 | The module's DbContext reads the tenant from the message context. An EF Core transaction interceptor runs `set_config('app.tenant_id', ..., true)` when the transaction starts | R4, R5 | T1, T2, T5, T6 |
| W3 | An endpoint on the tenant path relies on the tenant middleware, which sets the tenant on the message bus, and uses `InvokeAsync`. Accepting an invitation uses `InvokeForTenantAsync` with the tenant from the code. Renaming, suspending, reactivating, closing, reopening and reading one tenant (`GetTenant`) use `InvokeForTenantAsync` with the tenant from the admin route. Deleting a user (`DELETE /v1/admin/users/{userId}`) uses `InvokeForTenantAsync` with each tenant that `ListUserMemberships` answered, which the user's own memberships prove (R11), for `GetSoleOwnership` and `RemoveUserMembership`. Creating a tenant passes the id the server chose to the idempotency executor. Inviting a member (`InviteMember`) passes the tenant of the route, which the tenant door has proved, to the idempotency executor. It uses `InvokeAsync` with `DeliveryOptions.TenantId` and the key's envelope headers (I4). Both Wolverine invocation methods put the tenant on the envelope. The tenant goes to the following messages automatically. A message without a tenant carries the value `*DEFAULT*`. The interceptor does not treat this value as a tenant | Requirement 7, R5. Wolverine, message bus invocation | T3, T6 |
| W4 | `MultipleHandlerBehavior.Separated` is on | O3 | T8 |
| W5 | Messages wait in durable queues, and the message store is in the shared `wolverine` schema. In the role `all`, a message goes to a durable local queue (`UseDurableLocalQueues`). In the roles `web` and `worker`, every message goes through the outbox to one PostgreSQL queue in the same schema (Wolverine's PostgreSQL transport). Only a worker listens to that queue, and it hands an event with several handlers on to durable local queues, one for each handler (W4). A web host runs no durability agent (`DurabilityAgentEnabled = false`): outbox and inbox recovery run in a worker | O1, O5, Requirement 5, section 1 | T3, `HostRoleTests` |
| W6 | A saga derives from Wolverine's `Saga` class. Its record is stored with EF Core in the module's own schema. The `Version` property is mapped as a concurrency token. There is a retry policy for `SagaConcurrencyException` | S3, S8 | T7, T9 |
| W7 | A business rule rejection returns before any data changes, at the start of the handler or in a Validate or Before method. A failure after a change is an exception | Wolverine also commits a handler that returns a failed `Result` | T10a, T10b |
| W8 | The transaction middleware is first in the Wolverine chain. Validation runs inside the transaction. Authorization runs before, in ASP.NET Core | This is Wolverine's behaviour. A rejected request opens an empty transaction and writes no data | Generated handler code |
| W9 | Handler, saga, message and DbContext types are `public` | Wolverine's handler code is in another assembly than the handler: the host's (W10), or one that a test host compiles (section 9, rule 11) | Compile errors CS0051 and CS0122 |
| W10 | The handler code is written before the build and committed. `pnpm api:codegen` runs Wolverine's `codegen write`, which writes it to `src/Api/Internal/Generated`. The host loads it as static types (`TypeLoadMode.Static`) and compiles nothing at runtime. The host has no `WolverineFx.RuntimeCompilation`. The command gives the host a database setting it never opens, because Wolverine orders the frames by the message storage the host has. The role and the environment change no code; `HandlerCodeFileTests` writes it in each role. Wolverine finds each type by its name and finds the handlers in the committed registry. It compares no code, so out-of-date code runs old frames or leaves a new handler out without an error. `HandlerCodeFileTests` writes the code again and fails when it differs from the committed files. A static host builds every handler when it starts. The OpenAPI document build and the architecture tests' composed hosts handle no message, so they use `TypeLoadMode.Auto` and load no handler | A host that compiled its handler code at runtime kept the compiled code and the compiler's copies of ten framework assemblies (about 25 MB) for the life of its process. The test process grew by 26 to 41 MB for each host. Wolverine, "Working with Code Generation" | `HandlerCodeFileTests`, `HostMemoryTests` |
| W11 | Every queued message retries an exception three times inline, after 100 ms, 500 ms and 2 s. It then goes to the dead letter queue. Message-specific rules run first. The tenant steps, the two invitation emails (`OwnerInvitationReady`, `MemberInvitationReady`), the calls to the identity provider (`OpenOwnerSignUp`, `OpenMemberSignUp`, `WithdrawSignUp`, `DeleteIdentityAccount`, with `ControlPlane:Clerk:RetryDelays`) and `SagaConcurrencyException` keep their rules. The global fallback declines an inline `InvokeAsync` envelope, which has no queue destination | Nygard, *Release It!*, Chapter 5 (bounded retries). Hohpe and Woolf, "Dead Letter Channel". Wolverine, "Error Handling" | `DefaultRetryTests`, `ExistingRetryPolicyTests` |
| W12 | A node with no handler for a message from the PostgreSQL queue does not drop it. A missing handler (`IMissingHandler`) puts it back in the queue's scheduled table, with the same id, after `Messaging:UnknownMessages:Delay` (30 s). The header `unknown-message-tries` counts the put-backs. After `Messaging:UnknownMessages:Tries` (10), the message goes to the dead letter queue. A message in a durable local queue goes to the dead letter queue at once: Wolverine marks it as handled after the missing handler runs, and that mark overwrites a put-back in the inbox. Wolverine's own control messages keep Wolverine's default. Each put-back is a warning, and each dead letter is an error, with the message type, the try and the trace id. W12 does not cover a handler that only the new version adds to a message the old version knows: an old worker hands such a message only to the handlers it knows (W4, W5) | During a rolling deploy an old worker takes a message only the new version handles. Wolverine 6.47 drops it without a log at warning level (`NoHandlerContinuation`). Hohpe and Woolf, "Dead Letter Channel" and "Invalid Message Channel". Nygard, *Release It!*, Chapter 5 (bounded retries) | `UnknownMessageTests` |

### HTTP idempotency

| # | Rule | Source |
| --- | --- | --- |
| I1 | Every POST that creates something or starts a process requires an idempotency key. An endpoint that only reads, or sets a state such as suspend or reactivate, does not. GET and DELETE take no key. An endpoint opts in with `IdempotencyKeyRequired` metadata. The OpenAPI document declares its header as required | Stripe, "Idempotent requests". RFC 9110, section 9.2.2 |
| I2 | The header is `Idempotency-Key`. It contains 1 to 255 visible ASCII characters. A missing or invalid key answers 400. Authorization runs before the key check | IETF Idempotency-Key draft 07. Stripe, "Idempotent requests" |
| I3 | The host owns `api.idempotency_keys`. A key is unique for the identity provider's user id and the key together. The row holds the method, route, SHA-256 of the body and route values, a lease and its expiry, the saved handler result, and its creation time. It holds no request body or tenant column | Brandur Leach, "Implementing Stripe-like Idempotency Keys in Postgres" |
| I4 | The claim is one short, atomic database statement before the handler runs. The endpoint calls `IIdempotencyExecutor`. The key, subject and lease travel in envelope headers. Middleware holds a row lock in the handler's Wolverine transaction. A postprocessor writes the handler's `Result<T>` and clears the lock through the handler's own DbContext, before Wolverine commits its transaction. The result and business data commit together. A command with no business DbContext uses `ApiDbContext` and Wolverine's own transaction frames. The layer opens or commits no handler transaction | Brandur Leach, atomic phases. W1. Wolverine, "Middleware", "After is before the commit" |
| I5 | A finished request with the same input replays the saved result. The endpoint maps it through the same pure function to HTTP. Status, body and Location stay the same. The reply carries `Idempotent-Replayed: true`. The saved result includes the first trace id so a replayed Problem Details body stays the same. A running request answers 409. Another body, route value, method or endpoint under the key answers 422 | Stripe, "Idempotent requests". IETF draft 07 |
| I6 | Validation failures are not saved. A business rejection is saved before any business change (W7). An exception rolls back the business transaction and releases the claim. A server failure from that exception saves no result. A retry runs again. Each endpoint's mapping is a pure function of its saved result | Stripe, "Idempotent requests". W7 |
| I7 | `Api:IdempotencyKeyRetention` defaults to 24 hours and is at least 24 hours. A worker hosted service sends a Wolverine cleanup message each hour. The handler deletes expired rows and keeps an active lease. The role all includes this worker service. A web host does not run it | Stripe, "Idempotent requests". Twelve-Factor, Factor VIII |

The claim expires after one minute so a crashed caller does not hold a key forever.
An expired, unfinished claim can be taken again only when no live handler holds its row lock.
Its new lease fences the old handler: if that handler can no longer update its claim, its whole transaction rolls back.
`CreateTenant_CommitFailsAfterTheResultWrite_RollsBackTheTenantAndResult` verifies this transaction boundary on the production code.

Sources: [Stripe](https://docs.stripe.com/api/idempotent_requests), [Brandur Leach](https://brandur.org/idempotency-keys), [IETF draft 07](https://greenbytes.de/tech/specs/draft-ietf-httpapi-idempotency-key-header-07.html), [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html#section-9.2.2).

### Concurrency

| # | Rule | Source |
| --- | --- | --- |
| K6 | Every handler that changes data, a saga step included, first locks the root row of the aggregate it changes (`SELECT ... FOR UPDATE`). Updating a tenant locks that tenant's row; updating an invoice would lock that invoice's row, never the tenant's. A handler that changes a membership or an invitation locks the tenant row, because they belong to the tenant aggregate (M1). A rule that only keeps a value unique uses a database constraint (a unique index), not a lock. Locks are taken in one order: the aggregate root first. The one row an endpoint locks before it is the host's idempotency claim (I4), which a second request with the same key never waits for: it answers 409. A lock lives only inside one transaction, never across two HTTP requests. Today `RenameTenant`, `SuspendTenant`, `ReactivateTenant`, `CloseTenant`, `ReopenTenant`, `AcceptInvitation`, `InviteMember`, `RevokeInvitation`, `ChangeMemberRole` and `RemoveMember` lock the tenant row, and `AcceptInvitation` and `RevokeInvitation` then lock the invitation, and `AcceptInvitation` then takes a share lock on a known user's row, so a deletion of that user runs before or after it (A8). A request passes the tenant door before it waits for the lock, so the tenant's status may change while it waits. Every tenant command that locks the tenant row (`InviteMember`, `RevokeInvitation`, `ChangeMemberRole`, `RemoveMember`) therefore asks again under the lock whether the tenant is active (`Tenant.CheckMemberAccess`, the rule the door asks), right after it checks the actor's membership, and refuses a suspended or a closed tenant with the door's answer. `CloseTenant` cancels every pending invitation under the same lock, so an invitation and a closure at once never leave a pending invitation on a closed tenant. `AcceptInvitation` refuses a suspended tenant after the invitation's own checks (section 6), and finds an invitation of a closed tenant cancelled. `InviteMember` checks the daily limit and replaces a pending invitation under the tenant's lock; the partial unique index on the tenant and `lower(email)` of pending invitations is the second guard. `ChangeMemberRole` and `RemoveMember` read the actor's and the target's memberships and count the Owners under the tenant's lock, so two changes at once never leave a tenant without an Owner. `SuspendUser`, `ReactivateUser`, `RenameUser`, `DeleteUser` and `ChangePreferences` lock the user row. A user's deletion leaves each tenant with `RemoveUserMembership`, which locks that tenant's row and counts the Owners again (section 6, "User deletion"). `RecordSignUpHandler` (`SignUpOpened`) and `ExpireSignUpHandler` lock the tenant row and then the invitation (section 6, "Invitation sign-up"). `CreateTenant` adds a new tenant, and the slug's unique index decides between two creations. The saga's steps `ActivateTenant`, `CancelTenant` and `CancelInvitation` lock the tenant row and then the invitation. Tests: `AcceptInvitation_WhileASuspensionHoldsTheTenant_WaitsAndIsThenAConflict`, `CancelInvitation_WhileAnotherTransactionHoldsTheTenant_WaitsForIt`, `InviteMember_TwoAtOnceToTheSameEmail_EndWithOnePendingInvitation`, `InviteMember_TwoAtOnceWithOnePlaceLeft_NeverPassTheLimit`, `ChangeMemberRole_TheOnlyTwoOwnersDemoteEachOtherAtOnce_LeaveOneOwner`, `RemoveMember_TheOnlyTwoOwnersRemoveEachOtherAtOnce_LeaveOneOwner`, `InviteMember_ByAnAdminRemovedWhileItWaited_IsNotFoundAndCreatesNothing`, `CloseTenantAndInviteMember_AtOnceWithTheClosureFirst_LeaveNoPendingInvitation`, `CloseTenantAndInviteMember_AtOnceWithTheInvitationFirst_LeaveNoPendingInvitation`, `TenantCommand_WaitingWhileTheTenantCloses_IsForbiddenAndChangesNothing`, `DeleteUser_BecomesTheLastOwnerWhileItRuns_StopsKeepsTheUserAndTheTenantsItLeftStayLeft`, `AcceptInvitation_WhileADeletionHoldsTheUser_WaitsAndIsRefused` | Kleppmann, *Designing Data-Intensive Applications*, Chapter 7, "Write Skew and Phantoms". Fowler, PoEAA, "Coarse-Grained Lock". Evans, Chapter 6, "Aggregates". Vernon, "Effective Aggregate Design". PostgreSQL documentation, "Explicit Locking" |

K6 keeps the id it has in the catalog, so the rule has one name in both documents.

### Saga rules

| # | Rule | Source |
| --- | --- | --- |
| S1 | The default is an event. There is no saga | Richardson, Chapter 4 |
| S2 | A saga is built only if three conditions are true together: the process covers more than one module or an external system, it needs compensation or a timeout, and someone asks "where is the process now" | Richardson, Chapter 4. Khononov, Chapter 9 |
| S3 | Saga state is kept in its own record. It is not hidden in a field of another entity | Hohpe and Woolf, Chapter 7 (Process Manager) |
| S4 | The type is orchestration | Richardson, Chapter 4 |
| S5 | There are three kinds of step: compensatable, pivot, retryable. Work that cannot be undone comes after the pivot | Richardson, Chapter 4 |
| S6 | The saga record and the outgoing message are written in the same transaction | O1 |
| S7 | Each step and each compensation can be repeated safely | Hohpe and Woolf, Chapter 10 |
| S8 | Only one message is handled for the same saga at a time. The version number on the record protects this | Fowler, PoEAA Chapter 16 |
| S9 | A transient failure is tried a limited number of times. Then compensation starts. If compensation also fails, the saga goes to the "needs attention" state and raises an alert | Nygard, Chapter 5 |
| S10 | Each wait has a timeout | Nygard, Chapter 5 (Timeouts) |
| S11 | Until the pivot, the tenant shows the "provisioning" state | Richardson, Chapter 4 (semantic lock) |
| S12 | The saga class is unit tested without a host. The saga does not call external services. It only decides and returns messages | Khorikov, Chapter 7 |
| S13 | A saga shares no message with another handler. It hears its steps through replies and faults sent to it, not through events meant for other modules. A test enforces it | Richardson, Chapter 4 (command and reply). Wolverine runs a saga alone at the endpoint its message arrives on |

"Tenant is provisioning" and "tenant is active" are the tenant's own business state and stay in its status. Step tracking and retry counts are not put in the status. They stay in the saga record.

The saga class is in the `Application` project. The `Domain` project does not see Wolverine.

### Cache rules

The rules are part of the architecture. The code is not in the first template.

1. The cache runs only on queries and is off by default.
2. The cache step comes after authorization.
3. The step adds the tenant id to the key. A handler cannot add it and cannot forget it.
4. Each entry has a lifetime and a memory limit.
5. When a command changes data, the related entry is removed.
6. With many pods, a shared cache is used or the lifetime is kept short.

## 4. Tenant source and isolation

The tenant id is in the API path. The id in the path does not give access. Verified identity and membership give access. The database enforces isolation.

### Tenant source

| # | Rule | Source |
| --- | --- | --- |
| T1 | The path is `/v1/tenants/{tenantId}/...`. The value is the id, not the slug. The slug is not used in the path | Azure Architecture Center, "Map requests to tenants". Google AIP-122 |
| T2 | The tenant id is not read from the body, the query string or a header. A handler gets the tenant from the message envelope. A saga's messages may carry the tenant id to find the saga; data access still uses the envelope's tenant | Golding, Chapter 7 |
| T3 | If there is no membership, the response is 404. Its body is the handlers' own `tenant.not_found`, apart from the trace id: the tenant directory answers a stranger with that error, so a caller cannot tell "not a member" from "no such tenant" by the status or the body, also for a suspended or a closed tenant (`TenantNotFoundTests`, `ClosedTenantTests`) | OWASP API Top 10, API1 |
| T4 | `GET /v1/me/tenants` runs without a tenant | Industry practice |
| T5 | Provider staff are not tenant members. Their door is the `/v1/admin/...` path | Golding, Chapter 2 |
| T6 | The token carries only the user identity. No tenant, role or permission comes from the identity provider | Fixed requirement: the identity provider is authentication only |
| T7 | Membership is verified on each request in one place. No tenant endpoint can go around this filter | OWASP API Top 10, API1 |

A tenant has two descriptive fields. `name` is free text, cannot be empty, has a maximum of 100 characters and does not have to be unique. `slug` is unique and is given when the tenant is created. `GET /v1/me/tenants` returns both, and the tenant's status. The path, authorization and isolation use only the id.

### Row Level Security

| # | Rule | Source |
| --- | --- | --- |
| R1 | Each table that belongs to a tenant has `tenant_id`. RLS is enabled and forced | Golding, Chapters 8 and 9 |
| R2 | The application account is not the table owner and cannot bypass RLS | Golding, Chapter 9. OWASP (least privilege) |
| R3 | Migrations run with a separate account. That account is not used at run time. The application does not start while a module misses a migration, and to check it the application account may read each module's migration history table | Twelve-Factor, Factor XII |
| R4 | The tenant is declared at the start of each transaction. A connection-level setting is not permitted. A transaction declares a tenant or a user, never both. A transaction declares one tenant only. | PostgreSQL connection pool behaviour |
| R5 | If the tenant is not declared, a query returns no data | Nygard, Chapter 5 (Fail Fast) |
| R6 | Tables without a tenant are on an explicit list: `control_plane.tenants`, `control_plane.users`, `control_plane.roles`, `control_plane.system_admins`, `api.idempotency_keys`, and `audit.platform_entries`, the platform's audit log, which has row level security of its own (R11). A test checks every table. The wolverine schema and the migration history tables are exempt by pattern. The wolverine tables hold message bodies, invitation links included, and are not under RLS (see Deviations). | Ford, Parsons, Kua, Chapter 2 |
| R7 | There are only two database accounts: migration and application. Running code has no path that bypasses RLS | Golding, Chapter 9 |
| R8 | A background handler gets the tenant from the message and declares it in the same way | Requirement 7 |
| R9 | Each tenant table is tested with real PostgreSQL: read, update, insert for another tenant | Khorikov, Chapter 10 |
| R10 | At startup the application checks its own account. If the account, or a role it is a member of, is a superuser, can bypass RLS or owns a table, the application does not start. The readiness check asks the same. | Nygard, Chapter 5 (Fail Fast). PostgreSQL documentation, Privileges |
| R11 | The membership table has one more policy, `own_memberships`: `FOR SELECT` only, on `user_id` = the declared user (`app.user_id`, set in the transaction like the tenant). It serves `GET /v1/me/tenants`, and a user's deletion, which declares the user to read their memberships (`ListUserMemberships`, `DeleteUser`). The platform's audit log, `audit.platform_entries`, has row level security, forced, with two policies: `platform_read`, `FOR SELECT` only when the transaction declares neither a tenant nor a user, and `platform_insert`, `FOR INSERT WITH CHECK (true)`, so Audit can add the platform's record in a tenant's transaction. No tenant route and no me route can read it. The application role has `SELECT` and `INSERT` only. Any policy other than `tenant_isolation` must be on an explicit list, and a test fails on any other. Tests: `TenantIsolationCoverageTests`, `TenantTableIsolationTests`, `PlatformAuditLogTests` | Postgres combines permissive policies with OR. A write policy would widen access |
| R12 | Deferred (see Deferred): the system admin sees tenant and member counts from ControlPlane's own summary data. The tenant list of step 19d has no member counts. Memberships are under RLS and the list reads across tenants, so a count needs that summary data, which does not exist yet | Golding, Chapter 2 |

Work across tenants goes through the tenant list and declares each tenant in turn. RLS does not protect a cache or data that leaves the database. An EF Core query filter is not added as a second layer. RLS is the single source of truth.

## 5. Identity and authorization

The code checks permissions, not roles. Provider staff enter through a separate door.

| # | Rule | Source |
| --- | --- | --- |
| A1 | Authorization is permission based. The code asks for a permission, not for a role name | OWASP API Top 10, API5. Golding, Chapter 6 |
| A2 | The built-in roles are Owner, Admin and Member. They are the same in each tenant and cannot be changed | Industry practice |
| A3 | A tenant builds its own role (custom role) from the fixed permission list. This feature is not in the first template | Industry practice |
| A4 | A system admin is not a tenant member. The staff list is in ControlPlane. It has its own permission list | Golding, Chapter 2 |
| A5 | Each endpoint carries one of three explicit states: public, signed-in only, requires a permission. An endpoint without a state breaks the architecture test. Each endpoint on the tenant path requires a permission. The API receives no webhook: nobody edits a user at the identity provider, so nothing flows back (A7) | OWASP API Top 10, API5 |
| A6 | The system door requires a second factor. The rule is in ControlPlane. The host only translates the identity provider's field into a neutral value. Tenant users are not affected | OWASP ASVS, item 4.3.1 |
| A7 | The catalog owns a user's email address, first name and last name. The identity provider only signs people in and keeps no name; T6 does not change. The email address is the invited one: accepting an invitation needs a verified address of the person that is the same, and the verified addresses come from the identity provider's server. The first system admin's is the configured address. The names come from the person: one the catalog does not know types them when they accept an invitation (required, 1 to 100 characters each, trimmed, else 400 `request.validation_failed` with `required` or `too_long`); a user the catalog knows sends none. After that only a system admin changes them, in the catalog only (`RenameUser`, `PATCH /v1/admin/users/{userId}`, `user.renamed`). The first system admin starts with no names, and with English (catalog, K8). The identity provider gets no name, no password and no profile, and the API receives no webhook. The API makes four calls to it and no other: let an address sign up, withdraw that permission, read a user's verified email addresses, delete a user (`IIdentityProvider`, adapter in `ControlPlane.Infrastructure/Clerk`) | Microsoft, Azure Architecture Center, "Federated Identity pattern". Evans, Chapter 14 (Anti-Corruption Layer). Kleppmann, Chapters 11 and 12 (system of record). GDPR, Article 5(1)(c) (data minimisation) |
| A8 | The catalog holds only what a screen needs: no image, no phone number, no metadata. Deleting a user is a system admin's explicit action (`DELETE /v1/admin/users/{userId}`), never automatic when the last membership goes. It refuses a system admin (409 `user.is_system_admin`) and the last Owner of an active, suspended or closed tenant (409 `user.last_owner`; `tenantIds` names those tenants). It runs in steps (section 6, "User deletion"). Only when the user has no membership left does it erase the email address, the names and the time zone, which tells where the person lives, and set `DeletedAt`, and write `user.deleted`; the identity provider deletes its user after that commit. The row stays, because audit entries point to it. An audit record holds no name and no email address, so nothing else is erased: the read side marks the user deleted, with no name, and the screen shows "Deleted user" in its own language (K3). A deleted user cannot be renamed (409 `user.deleted`), and is refused on every `me` and tenant endpoint (403 `user.deleted`, A9) until their session ends | GDPR, Article 5(1)(c) (data minimisation) and Article 17. Kleppmann, Chapter 7, "Write Skew and Phantoms" |
| A9 | A system admin can suspend a catalog user. A suspended user gets 403 `user.suspended` on every `me` and `tenant` endpoint, accepting an invitation included. The check runs on every request, in one place: `TenantResolutionMiddleware` asks ControlPlane (`IUserDirectory`) for an endpoint with the `me` or the tenant metadata, and authorization turns the user away. It runs before the membership, so a suspended user who is also in a suspended tenant gets `user.suspended`. A deleted user gets 403 `user.deleted` in the same check (A8). No endpoint can go around it: a test fails for a `me` or tenant endpoint without its group's metadata, or with anonymous access (`SuspendedUserRoutesTests`). The admin door is not checked: a system admin cannot be suspended (409 `user.is_system_admin`), so the staff keep their door. The identity provider is not told; the user can still sign in, and our tables refuse them | OWASP API Top 10, API1 and API5 (one check no endpoint skips, as T7). NIST SP 800-53 Rev. 5, AC-2 (disable accounts) |

Tenant users and staff sign in with the same identity provider instance. Our tables decide which door a person can use. Terms: "system admin" (Golding), "built-in roles" and "custom roles".

## 6. Modules and tenant creation

There are three modules. A system admin creates the tenant and the first Owner comes by invitation.

| Module | What it holds |
| --- | --- |
| ControlPlane | Tenant record, system admins, users, memberships, roles and permissions, invitations, tenant creation, the tenant lifecycle (renaming, suspension, closure, reopening), user suspension |
| Notifications | It hears an event and sends a message to a person. In the first template, only the invitation emails: to a new tenant's first Owner, and to a person a member invited |
| Audit | It hears an event and writes the record of "who did what to what, and when", in the tenant's log, the platform's log, or both (K3). It serves the two logs to their readers (section 6, "Reading the audit logs") |

Source: Golding, Chapter 2 (control plane). Khononov, Chapter 3 (when you are not sure, keep the boundary wide).

| # | Rule | Source |
| --- | --- | --- |
| M1 | In ControlPlane, memberships and invitations belong to the tenant aggregate. "A tenant always has at least one Owner" is the tenant's rule (catalog, K1), so the Owner rules of a role change, a removal and a user's deletion are methods of `Tenant`: `ChangeMemberRole`, `CheckRemoval` and `CheckUserDeletion`. The code keeps its types and folders as they are; the aggregate decides what a change locks (K6). Tests: the test of K6, `MemberManagementTests` | Evans, Chapter 6, "Aggregates". Vernon, "Effective Aggregate Design" |

### Tenant creation process

The process is a saga. Three steps run in order.

The saga is part of the template, and tenant creation is its reference use. It meets all three conditions of S2: the process covers ControlPlane, Notifications, the identity provider and an email service, it needs compensation and timeouts, and someone asks where it is. A product built on the template keeps it. A new saga, or a new step, must meet S1 and S2 too.

1. ControlPlane writes the tenant ("provisioning"), the Owner invitation and the saga record in one transaction. The invitation code and its link are created here. Compensatable.
2. The tenant becomes "active". Pivot.
3. The identity provider lets the owner's address sign up, then Notifications sends the invitation email with its link ("Invitation sign-up", below). Each call is tried a limited number of times, with growing pauses.

The Owner signs up with the identity provider like any invited person, through its invitation. Tenant creation creates no tenant, role or user in the identity provider.

Tenant creation messages (N1 to N3):

| Message | Sent by | Handled by | Handler has a transaction |
| --- | --- | --- | --- |
| `CreateTenant` | `POST /v1/admin/tenants` | ControlPlane, `CreateTenantHandler` (step 1, starts the saga) | Yes |
| `ActivateTenant` | Step 1 | ControlPlane, `ActivateTenantHandler` (step 2, pivot) | Yes |
| `ActivationTimedOut` | Step 1, scheduled | ControlPlane, `TenantCreationSaga` | Yes |
| `TenantActivated` | Step 2 | Audit, `RecordTenantCreatedHandler` (`tenant.created`, both logs) | Yes |
| `ActivateTenantReply` | Step 2 | ControlPlane, `TenantCreationSaga` | Yes |
| `OpenOwnerSignUp` | Step 2 | ControlPlane, `OpenSignUpHandler` (step 3, calls the identity provider) | No |
| `SignUpOpened` | `OpenSignUpHandler` | ControlPlane, `RecordSignUpHandler` (step 3, records the identity provider's invitation) | Yes |
| `OwnerInvitationReady` | `RecordSignUpHandler` | Notifications, `SendInvitationEmailHandler` (step 3, calls the email service) | No |
| `InvitationEmailTimedOut` | Saga, scheduled | ControlPlane, `TenantCreationSaga` | Yes |
| `SendInvitationEmailReply` | Step 3 | ControlPlane, `TenantCreationSaga` | Yes |
| `CancelTenant` | Saga | ControlPlane, `CancelTenantHandler` (compensation) | Yes |
| `CancelTenantReply` | `CancelTenantHandler` | ControlPlane, `TenantCreationSaga` | Yes |
| `CancellationTimedOut` | Saga, scheduled | ControlPlane, `TenantCreationSaga` | Yes |
| `CancelInvitation` | Saga | ControlPlane, `CancelInvitationHandler` (compensation) | Yes |
| `RaiseTenantCreationAlarm` | Saga | ControlPlane, `TenantCreationAlarmHandler` | No |
| `Fault<T>` of a step or a compensation | Wolverine, when the message goes to the dead letter queue | ControlPlane, `TenantCreationSaga` | Yes |

The saga's record is the table `control_plane.tenant_creation_sagas`. The alarm is the error log event `TenantCreationNeedsAttention` and the counter `tenant_creation.needs_attention`.

- If step 2 fails for good, the tenant is cancelled. A reason code is stored on the tenant and logged. The tenant list for the system admin shows the status `cancelled`, not the reason.
- If step 3 fails for good, or a wait after step 1 times out, the process goes to "needs attention" and the invitation is cancelled. The alarm is raised once. Step 3 fails for good when the identity provider's call goes to the dead letter queue (`Fault<OpenOwnerSignUp>`) or the email does (`Fault<OwnerInvitationReady>`). After the activation the saga waits for the email's report (`SendInvitationEmailReply`), within `ControlPlane:InvitationEmailTimeout`, which is longer than both calls' retries together (`setup.md`).
- The invitation link travels to Notifications inside the message. See the deviations list.
- The invitation link carries one invitation code: the tenant id and a secret, `<tenantId>.<secret>`. Only the hash of the secret is stored. Accepting declares the tenant from the code, then finds the invitation by the hash in that tenant. A missing or empty code answers 400. A wrong tenant, a wrong secret and a malformed code all answer 404. This is the only place where a request names its tenant outside the path. The secret gives the right, not the tenant id.
- Accepting an invitation needs two things: the secret in the code, and a verified email address of the signed-in user that is the same as the invited address. The verified addresses come from the identity provider's server, not from the request.
- The email handler is in `Notifications.Application`. The event type is in `ControlPlane.Contracts`. ControlPlane does not know how to send email.
- The cancelled invitation's identity provider invitation is withdrawn (`CancelInvitation` returns `WithdrawSignUp`).
- Invitation expiry is checked when the invitation is read. There is no nightly job.
- The first system admin comes from an email address in configuration. The application writes it only while the staff list is empty.

Source: Golding, Chapter 4. Richardson, Chapter 4.

### Invitation sign-up

The identity provider is invite-only: only its own invitation lets a person without an account sign up. Our invitation stays ours: its tenant, role, lifetime, audit and email. The identity provider's invitation only lets the invited address sign up (A7). This is not a saga: it follows the invitation through events and retried handlers (S1).

| Message | Sent by | Handled by | Handler has a transaction |
| --- | --- | --- | --- |
| `OpenOwnerSignUp` | `ActivateTenantHandler` (tenant creation, step 3) | ControlPlane, `OpenSignUpHandler` (calls the identity provider) | No |
| `OpenMemberSignUp` | `InviteMemberHandler` | ControlPlane, `OpenSignUpHandler` (calls the identity provider) | No |
| `SignUpOpened` | `OpenSignUpHandler` | ControlPlane, `RecordSignUpHandler` | Yes |
| `OwnerInvitationReady`, `MemberInvitationReady` | `RecordSignUpHandler` | Notifications, `SendInvitationEmailHandler`, `SendMemberInvitationEmailHandler` (call the email service) | No |
| `ExpireSignUp` | `RecordSignUpHandler`, scheduled at the invitation's expiry | ControlPlane, `ExpireSignUpHandler` | Yes |
| `WithdrawSignUp` | `RecordSignUpHandler`, `ExpireSignUpHandler`, `InviteMemberHandler` (a replaced invitation), `RevokeInvitationHandler`, `CloseTenantHandler`, `CancelInvitationHandler` | ControlPlane, `WithdrawSignUpHandler` (calls the identity provider) | No |

- The invitation and its event commit first, with the command that opens the sign-up, through the outbox. No call to the identity provider runs inside a transaction (section 3).
- `OpenSignUpHandler` asks the identity provider to let the address sign up until our invitation's expiry, with our accept link, which carries the invitation code. An address with an account needs no invitation: the answer is the accept link itself. Otherwise the answer is the identity provider's link and its invitation's id.
- `RecordSignUpHandler` locks the tenant row and then the invitation (K6), and decides with `Invitation.RecordSignUp`. A pending invitation without an identity provider invitation keeps the id, and the link goes to Notifications. An answer that arrives twice changes nothing; for an address with an account, which has no identity provider invitation to compare, it publishes the email's event again, and the email's idempotency key (`invite/` and the invitation id) sends one email. An answer for an invitation that was revoked, replaced, cancelled or accepted meanwhile, or for one that already has another identity provider invitation, sends no email, and its identity provider invitation is withdrawn. So no email carries a link to an invitation that ended, and nobody can sign up with a link nobody received.
- With the link, `RecordSignUpHandler` schedules `ExpireSignUp` at the invitation's expiry. The identity provider counts an invitation's life in whole days and rounds ours up, so this withdraws its invitation when ours ends. The module's clock decides: a message that arrives before the expiry withdraws nothing. An invitation that ended before withdrew it then; an accepted one needs nothing.
- Revoking, replacing and cancelling an invitation, and closing its tenant, withdraw its identity provider invitation through `WithdrawSignUp`, after the commit. Accepting withdraws nothing.
- The calls have the retries of `ControlPlane:Clerk:RetryDelays`, as the invitation emails have theirs (W11). Each of these messages has one handler, so a retry may be scheduled (W5). After the last try the message goes to the dead letter queue: no email goes out, and a member's invitation stays pending; the inviter can invite again, which replaces it (K2). For a tenant's first owner the saga hears of it (`Fault<OpenOwnerSignUp>`).
- The identity provider redirects to our accept link and adds `__clerk_ticket`. The web signs the person up with the ticket, which verifies the invited address, and then accepts with the code (step 21).
- Each new message type comes with all its handlers in the same release, so the gap of W12 does not apply.

Sources: Microsoft, Azure Architecture Center, "Federated Identity pattern". Richardson, Chapter 3 (transactional outbox). Clerk, "createInvitation" and "Sign-up with application invitations".

### Tenant lifecycle

A system admin renames a tenant, suspends it for a reason and reactivates it, closes it and reopens it (catalog, K4). This is not a saga: one module changes one row, and closing also cancels the tenant's pending invitations in the same transaction (S1). Each command runs in the tenant's own transaction, with the tenant declared from the admin route (W3), and locks the tenant row (K6). Each sets a state, so none takes an idempotency key (I1).

The moves (K4):

- provisioning → active (the saga's pivot), provisioning → cancelled (its compensation)
- active → suspended (`SuspendTenant`), suspended → active (`ReactivateTenant`)
- active → closed and suspended → closed (`CloseTenant`)
- closed → active (`ReopenTenant`), only while `closedAt` plus `ControlPlane:Tenants:ReopenPeriod` (30 days by default) is in the future
- deleted is a later step: nothing deletes a tenant, and no job runs when the reopen period ends

| Stored status | `POST .../suspend` | `POST .../reactivate` | `POST .../close` | `POST .../reopen` | `PATCH` (rename) |
| --- | --- | --- | --- | --- | --- |
| `Active` | Becomes `Suspended`, with the reason | No change | Becomes `Closed` at this moment | No change | Renamed |
| `Suspended` | No change, the first reason stays | Becomes `Active`, the reason is cleared | Becomes `Closed`, the reason stays | 409 `tenant.not_closed` | Renamed |
| `Closed` | 409 `tenant.not_active` | 409 `tenant.not_active` | No change, the first moment stays | Becomes `Active`, `closedAt` and the reason are cleared; after the period 409 `tenant.reopen_period_ended` | 409 `tenant.not_active` |
| `Provisioning`, `Cancelled` | 409 `tenant.not_active` | 409 `tenant.not_active` | 409 `tenant.not_active` | 409 `tenant.not_closed` | 409 `tenant.not_active` |

Every success answers 200 with the tenant as the system admin sees it (`TenantResponse`). "No change" answers 200 too, and publishes nothing. Renaming to the name the tenant has is no change either.

| Message | Sent by | Handled by | Handler has a transaction |
| --- | --- | --- | --- |
| `RenameTenant` | `PATCH /v1/admin/tenants/{tenantId}` | ControlPlane, `RenameTenantHandler` | Yes |
| `SuspendTenant` | `POST /v1/admin/tenants/{tenantId}/suspend` | ControlPlane, `SuspendTenantHandler` | Yes |
| `ReactivateTenant` | `POST /v1/admin/tenants/{tenantId}/reactivate` | ControlPlane, `ReactivateTenantHandler` | Yes |
| `CloseTenant` | `POST /v1/admin/tenants/{tenantId}/close` | ControlPlane, `CloseTenantHandler` | Yes |
| `ReopenTenant` | `POST /v1/admin/tenants/{tenantId}/reopen` | ControlPlane, `ReopenTenantHandler` | Yes |
| `TenantRenamed` | `RenameTenantHandler`, on a change of name | Audit, `RecordTenantRenamedHandler` (`tenant.renamed`, both logs) | Yes |
| `TenantSuspended` | `SuspendTenantHandler`, on a change of status | Audit, `RecordTenantSuspendedHandler` (`tenant.suspended`, both logs) | Yes |
| `TenantReactivated` | `ReactivateTenantHandler`, on a change of status | Audit, `RecordTenantReactivatedHandler` (`tenant.reactivated`, both logs) | Yes |
| `TenantClosed` | `CloseTenantHandler`, on a change of status | Audit, `RecordTenantClosedHandler` (`tenant.closed`, both logs) | Yes |
| `TenantReopened` | `ReopenTenantHandler`, on a change of status | Audit, `RecordTenantReopenedHandler` (`tenant.reopened`, both logs) | Yes |

- A request that changes nothing publishes nothing, so Audit records each change once.
- The name is trimmed and checked as at the creation: 1 to 100 characters, else 400 `tenant.name_invalid`. The slug never changes. Only the platform renames a tenant: the tenant path has no endpoint for the name.
- The suspension's reason is text the system admin writes: required, 1 to 500 characters, trimmed (`SuspendTenantValidator`, field codes `required` and `too_long`). It is stored on the tenant (`suspension_reason`) and shown on the admin side only (`GetTenant`, `ListTenants`). The tenant door answers a member `tenant.suspended`, without it. Only the platform's audit entry holds it: the tenant's log is read by the tenant's Owners and Admins (`ListAuditEntries`), so its entry has no details.
- A member who calls a tenant route gets 403 `tenant.suspended` or `tenant.closed`. A non-member still gets 404 (T3): only a member learns that the tenant is suspended or closed. The rule is the tenant's (`Tenant.CheckMemberAccess`); the door asks it on every request (T7), and every tenant command asks it again under the tenant's lock (K6).
- `GET /v1/me/tenants` lists active, suspended and closed tenants, each with its status. Provisioning and cancelled tenants stay hidden.
- Closing stores `closedAt` and cancels every pending invitation of the tenant, expired ones included, in the same transaction. Accepting a cancelled invitation answers 404 `invitation.not_found`, as a revoked one (K2), also after a reopening: a reopening does not bring them back. A closure writes one audit entry in each log, `tenant.closed`, and no `invitation.revoked` entry.
- Accepting an invitation to a suspended tenant answers 409 `tenant.suspended` only for a valid invitation and the right person, as T3. The order is: not found or cancelled (404), staff (422 `invitation.staff_cannot_join`), already a member (409 `membership.exists`), the invitation's own checks (`invitation.not_pending`, `invitation.expired`, `invitation.email_mismatch`), then `tenant.suspended`, then the change. The acceptance locks the tenant row before the invitation (K6), so a suspension or a closure commits before the check or after the change, never between them. A stranger with the link keeps the invitation's own answer, and an expired invitation stays dead after a reactivation. The invitation's checks are a pure method (`CheckAcceptance`) that `Accept` also uses, so the refusal comes before any change (W7). The invitation of a suspended tenant stays pending and can be accepted after the reactivation.
- Messages already in flight for the tenant still run.
- A suspended or a closed tenant is past the pivot: a late compensation does not cancel it.
- Reactivation and reopening give the members their access back as before.
- Each new message type comes with all its handlers in the same release, so the gap of W12 does not apply.

Source: Golding, Chapter 2 (control plane, tenant management).

### Member invitation

An Owner or an Admin invites someone to the tenant, lists the pending invitations and revokes one (catalog, K1 and K2). This is not a saga: ControlPlane changes the tenant aggregate in one transaction, and the identity provider's sign-up and the email follow it (S1, "Invitation sign-up"). No saga waits for the email.

| Message | Sent by | Handled by | Handler has a transaction |
| --- | --- | --- | --- |
| `InviteMember` | `POST /v1/tenants/{tenantId}/invitations` | ControlPlane, `InviteMemberHandler` | Yes |
| `MemberInvited` | `InviteMemberHandler` | Audit, `RecordMemberInvitedHandler` (`member.invited`, tenant log) | Yes |
| `OpenMemberSignUp` | `InviteMemberHandler` | ControlPlane, `OpenSignUpHandler` ("Invitation sign-up") | No |
| `MemberInvitationReady` | `RecordSignUpHandler` | Notifications, `SendMemberInvitationEmailHandler` (calls the email service) | No |
| `RevokeInvitation` | `DELETE /v1/tenants/{tenantId}/invitations/{invitationId}` | ControlPlane, `RevokeInvitationHandler` | Yes |
| `InvitationRevoked` | `RevokeInvitationHandler`, only for a pending invitation | Audit, `RecordInvitationRevokedHandler` (`invitation.revoked`, tenant log) | Yes |

- `InviteMember` locks the tenant row (K6). It then checks, in this order: an inviter who is no longer a member (404 `tenant.not_found`, T3; a removal that held the lock first took their membership), a tenant that is no longer active (403 `tenant.suspended` or `tenant.closed`; a suspension or a closure that held the lock first changed it), only an Owner invites an Owner (403 `invitation.owner_requires_owner`, a Domain rule, `Invitation.CheckInviter`), an email that belongs to a member (409 `membership.exists`), and the daily limit (429 `invitation.daily_limit_reached`). Every rejection returns before any change (W7).
- The email address is trimmed and checked with FluentValidation's `EmailAddress`, as a tenant's first owner's is.
- A pending invitation to the same address, compared without case, becomes `Cancelled` and is saved first. Then the new invitation is added with a new secret. The partial unique index never sees two pending invitations.
- `MemberInvited` carries only the ids and the role, for Audit (K3). `OpenMemberSignUp` carries the address, the tenant's name and the accept link, which leads to the email once the identity provider has answered ("Invitation sign-up").
- The invitation keeps the language of its email: the request's `language`, else the inviter's own ("Language and time zone").
- `MemberInvitationReady` has one handler, with the retries of the owner invitation email (`Notifications:InvitationEmailRetryDelays`). After the last try the event goes to the dead letter queue, and the invitation stays pending. The email's idempotency key is the invitation's (`invite/` and its id), so an event that arrives twice sends one email.
- A replaced invitation's identity provider invitation is withdrawn, and so is a revoked one's (`WithdrawSignUp`).
- `RevokeInvitation` locks the tenant row. A revoker who is no longer a member answers 404 `tenant.not_found` (T3), and a tenant that is no longer active 403 `tenant.suspended` or `tenant.closed`. It then locks the invitation. A pending invitation, expired or not, becomes `Cancelled`. A cancelled one answers 204 and publishes nothing (I1). An accepted one answers 409 `invitation.not_pending`. Row level security hides another tenant's invitation, so its id answers 404 `invitation.not_found`.
- Each new message type comes with all its handlers in the same release, so the gap of W12 does not apply.

Source: Golding, Chapter 2 (control plane, tenant management). Hohpe and Woolf, "Command Message" and "Dead Letter Channel".

### User deletion

A system admin deletes a user (A8). A user's memberships are under row level security, and a transaction declares one tenant (R4), so no transaction can check every tenant and change them together. The deletion runs in steps, each in a transaction of its own; the endpoint sends them one after the other. This is not a saga: the request waits for each step and answers with the result (S1).

| Message | Sent by | Handled by | Handler has a transaction |
| --- | --- | --- | --- |
| `ListUserMemberships` | `DELETE /v1/admin/users/{userId}` | ControlPlane, `ListUserMembershipsHandler` | Yes; it declares the user (R11) |
| `GetSoleOwnership` | the same endpoint, once for each tenant the user owns that is active, suspended or closed | ControlPlane, `GetSoleOwnershipHandler` | Yes, the tenant's |
| `RemoveUserMembership` | the same endpoint, once for each tenant the user is a member of | ControlPlane, `RemoveUserMembershipHandler` | Yes, the tenant's |
| `UserMembershipRemoved` | `RemoveUserMembershipHandler` | Audit, `RecordMemberRemovedHandler` (`member.removed`, tenant log, by the platform) | Yes |
| `DeleteUser` | the same endpoint, last | ControlPlane, `DeleteUserHandler` | Yes; it declares the user (R11) |
| `UserDeleted` | `DeleteUserHandler`, only for the first deletion | Audit, `RecordUserChangesHandler` (`user.deleted`, platform log) | Yes |
| `DeleteIdentityAccount` | `DeleteUserHandler` | ControlPlane, `DeleteIdentityAccountHandler` (calls the identity provider) | No |

1. The check. `ListUserMemberships` finds the user (404 `user.not_found`), refuses a system admin (409 `user.is_system_admin`) and lists the user's memberships, by tenant id. For each tenant the user owns that is active, suspended or closed, `GetSoleOwnership` asks the tenant's rule (`Tenant.CheckUserDeletion`, K1), without a lock. If the user is the last Owner of any, the answer is 409 `user.last_owner`, `tenantIds` names those tenants, and nothing changes.
2. The removals. For each membership, `RemoveUserMembership` locks the tenant row (K6), counts the Owners again under the lock, and removes the membership. If the user became the tenant's last Owner since the check, it changes nothing there and the request stops with 409 `user.last_owner` for that tenant. The tenants the user already left stay left; the user is not erased.
3. The erasure. `DeleteUser` locks the user row, declares the user and reads the memberships again. A membership that appeared since, such as an invitation accepted at the same moment, stops it with 409 `user.has_memberships`; a second deletion removes it. An acceptance takes a share lock on the user's row and refuses a deleted user (403 `user.deleted`), so it either commits before this step reads the memberships or finds the user deleted. With none left, it erases the email address, the names and the time zone, sets `DeletedAt`, and publishes `UserDeleted` and `DeleteIdentityAccount` in the same transaction.
4. After the commit, `DeleteIdentityAccountHandler` deletes the user at the identity provider, with the retries of `ControlPlane:Clerk:RetryDelays`. A second deletion of a deleted user answers 204, records nothing again, and asks the identity provider again.

A deleted user is never a member and never an Owner. A deleted user is refused at the door until their session ends (A9), so they cannot accept an invitation meanwhile. Tests: `UserDeletionTests`.

Source: Kleppmann, Chapter 7, "Write Skew and Phantoms". Vernon, "Effective Aggregate Design". GDPR, Article 17.

### Member management

An Owner or an Admin changes a member's role and removes a member (catalog, K1). This is not a saga: ControlPlane changes the tenant aggregate in one transaction (S1).

| Message | Sent by | Handled by | Handler has a transaction |
| --- | --- | --- | --- |
| `ChangeMemberRole` | `PATCH /v1/tenants/{tenantId}/members/{userId}` | ControlPlane, `ChangeMemberRoleHandler` | Yes |
| `MemberRoleChanged` | `ChangeMemberRoleHandler`, only when the role changed | Audit, `RecordMemberRoleChangedHandler` (`member.role_changed`, tenant log) | Yes |
| `RemoveMember` | `DELETE /v1/tenants/{tenantId}/members/{userId}` | ControlPlane, `RemoveMemberHandler` | Yes |
| `MemberRemoved` | `RemoveMemberHandler` | Audit, `RecordMemberRemovedHandler` (`member.removed`, tenant log) | Yes |

- Both commands lock the tenant row (K6). They then read the actor's and the target's memberships and count the Owners, in this order:
  1. The actor is no longer a member, because a change that held the lock before removed them: 404 `tenant.not_found`. The tenant door answers a non-member the same 404 `tenant.not_found` (T3). `InviteMember` and `RevokeInvitation` check the same first (section 6, "Member invitation").
  2. The tenant is no longer active, because a suspension or a closure held the lock before: 403 `tenant.suspended` or `tenant.closed`, the door's answer (section 6, "Tenant lifecycle"). `InviteMember` and `RevokeInvitation` check the same second.
  3. No target membership in the tenant: 404 `membership.not_found`. Row level security hides another tenant's members.
  4. `RemoveMember` on yourself: 403 `membership.cannot_remove_self`.
  5. An Admin who acts on an Owner, or who gives the Owner role: 403 `membership.owner_requires_owner`.
  6. The change would leave no Owner: 409 `membership.last_owner`.
  7. The same role (`ChangeMemberRole` only): 200, and nothing is published (I1).
- The rules of checks 4 to 7 are the tenant's (M1): `Tenant.ChangeMemberRole` and `Tenant.CheckRemoval`, pure until the change. Check 2 is the tenant's too (`Tenant.CheckMemberAccess`). Every rejection returns before any change (W7).
- The Owner count holds every Owner membership, a deleted user's included (catalog, K1).
- A member may change their own role under the same rules. Nobody removes themselves, so a removal never meets check 6 one request at a time: the actor is an Owner, and a second Owner is the target. Check 6 guards the count.
- `ChangeMemberRole` answers the member, as `ListMembers` shows them. `RemoveMember` answers 204. Neither takes an idempotency key: both set a state (I1).
- A removed member loses access at once: the tenant door reads the membership on every request (T7). No cache holds it.
- Each new message type comes with all its handlers in the same release, so the gap of W12 does not apply.

Source: Evans, Chapter 6, "Aggregates". Vernon, "Effective Aggregate Design". Kleppmann, Chapter 7, "Write Skew and Phantoms".

### Language and time zone

The web speaks English, Turkish and Arabic. The API sends codes and data, and the screen writes every text in its own language (K5). The API writes text only in emails (catalog, K8).

| Message | Sent by | Handled by | Handler has a transaction |
| --- | --- | --- | --- |
| `ChangePreferences` | `PATCH /v1/me/preferences` | ControlPlane, `ChangePreferencesHandler` | Yes; it declares nothing (R6) |
| `UserPreferencesChanged` | `ChangePreferencesHandler`, only when a value changed | Audit, `RecordUserChangesHandler` (`user.preferences_changed`, platform log) | Yes |

- One list: `Language` in `SharedKernel` (section 2), `en`, `tr` and `ar`. JSON, the messages and the database keep a language as its tag. `ExactNameEnumConverter` (section 2) reads an exact tag only: never a number, a list of names with commas or a tag with spaces, which the default enum converter would take. So an unknown tag is a body the endpoint cannot read (400 `request.invalid`), as an unknown role is. The OpenAPI document has the one schema `Language` (N11).
- A user keeps `language`, required, and `timeZone`, an IANA name such as `Europe/Dublin`, or null for "not set": the web then uses the browser's time zone. `GetMe` answers both. `ChangePreferences` sets both: it locks the user row (K6), refuses a deleted user under the lock (403 `user.deleted`, A9), and the values the user has change nothing and publish nothing (I1).
- The API never interprets a time zone. The image runs in invariant globalization mode and has no time zone data (section 7). `ChangePreferencesValidator` checks the shape only: at most 64 characters (`too_long`), parts of ASCII letters, digits, `_`, `-` and `+`, joined by single slashes (`time_zone_invalid`). The web offers the list. The API formats no date or time.
- A new user starts with the language of the invitation they accept (`AcceptInvitation`). A known user keeps theirs. The first system admin starts with `en`.
- `InviteMember` and `CreateTenant` take an optional `language` for the invitation's email. Without it, the invitation takes the inviter's own; for `CreateTenant`, the system admin's. The invitation keeps it (`control_plane.invitations.language`).
- `RecordSignUpHandler` puts the invitation's language and lifetime (its expiry minus its creation) into `OwnerInvitationReady` and `MemberInvitationReady`. Notifications writes the email in that language, in plain text, from one block of texts for each language (`InvitationEmailTexts`). The email says how long the link works, in whole days, with each language's plural forms (Unicode CLDR, "Language Plural Rules"), and shows no date or time, so it needs no time zone. `ControlPlane:Invitations:Lifetime` is at least one day, so the email never says "0 days".
- A message written before step 20j has no language and no lifetime: its email is English, without the lifetime (`InvitationEmailLanguageTests`). The migration gives every user and invitation before it `en`, and the column keeps the default, so the version that serves during the deploy writes English users and invitations. A worker of that version still sends English emails for the invitations it handles until it stops.

Sources: W3C, "Language tags in HTML and XML" and "Working with time zones". IANA Time Zone Database, "Theory and pragmatics of the tz code and data". Microsoft Learn, "Globalization invariant mode". RFC 5646, section 4.4.1 (the column holds 35 characters).

### Audit

| # | Rule | Source |
| --- | --- | --- |
| K3 | Every command writes audit. A command is a message that an endpoint sends, and that changes state. A query (`List...`, `Get...`) and a step inside the saga are not commands. Each command publishes, through the outbox, one event that Audit handles, named in the past tense, in `ControlPlane.Contracts`. A command that changes nothing publishes nothing, so a repeated request writes no second record. `CreateTenant` is recorded when the saga's pivot makes the tenant active (`TenantActivated`): a creation that is cancelled before the pivot writes no record. An audit record holds who acted, the operation (`member.joined`, `user.suspended`), the target's kind and id, and when. It stores no personal name and no email address: the actor is a catalog user id, a target person is an id, and only a target that is not a person keeps its name of that moment (a tenant's name). A `member.invited` record stores the invitation id, not the email address. The read side resolves names in one batch through `IUserNames` in `ControlPlane.Contracts` (API composition); Audit never reads `control_plane` tables. When a system admin deletes a user, `DeleteUser` erases the catalog's email address and names, so the read side gives the user no name and marks them deleted, and no audit record changes. A record is never changed once written: the application role has `SELECT` and `INSERT` only. Tests: `CommandAuditTests`, `ChangeAuditEntry_AsTheApplication_IsRefused`, `DeleteUser_UserNamedInTheAuditLog_LeavesNoNameOrEmailInAnyAuditTableAndReadsAsDeletedUser` | GDPR, Articles 4(5) and 25 (pseudonymisation). GDPR, Article 17. Richardson, *Microservices Patterns*, "API composition" |

K3 has two logs. The tenant's log is `audit.entries`, under row level security as every tenant table (R1). The platform's log is `audit.platform_entries`, without a tenant column (R6, R11). A system admin's action goes to the platform's log, with the admin's catalog user id. Both logs keep an actor id as a uuid. Step 20f changed the tenant's log's column in place (`ALTER COLUMN ... TYPE uuid USING actor_id::uuid`); a value that is not a uuid fails the cast, and the migration's transaction rolls back. When it changes a tenant, the same Audit handler also writes the tenant's log, in the same transaction: there the actor kind is `Platform` and there is no actor id. Before step 20i the identity provider's webhook wrote records to the platform's log with the actor kind `IdentityProvider`. Nothing writes that kind now; a record is never changed, so the read side still names it. The event's id is the record's id in each log, so an event that arrives twice gives one record in each log. A record written before K3 has no actor kind and no target: it was a system admin's action on its tenant, and the read side shows it as the platform's. Sources: Golding, Chapter 2 (control plane). Hohpe and Woolf, Chapter 10 (Idempotent Receiver).

K3 keeps the id it has in the catalog. The audit records of the commands:

| Command | Event | Audit handler | Log | Operation | Actor |
| --- | --- | --- | --- | --- | --- |
| `CreateTenant` | `TenantActivated` (saga step 2) | `RecordTenantCreatedHandler` | platform and tenant | `tenant.created` | the system admin; `Platform` in the tenant's log |
| `RenameTenant` | `TenantRenamed`, only when the name changed | `RecordTenantRenamedHandler` | platform and tenant | `tenant.renamed`; the target keeps the new name; the details hold the old and the new name in both logs | as above |
| `SuspendTenant` | `TenantSuspended` | `RecordTenantSuspendedHandler` | platform and tenant | `tenant.suspended`; the details hold the reason in the platform's log only | as above |
| `ReactivateTenant` | `TenantReactivated` | `RecordTenantReactivatedHandler` | platform and tenant | `tenant.reactivated` | as above |
| `CloseTenant` | `TenantClosed` | `RecordTenantClosedHandler` | platform and tenant | `tenant.closed` | as above |
| `ReopenTenant` | `TenantReopened` | `RecordTenantReopenedHandler` | platform and tenant | `tenant.reopened` | as above |
| `SuspendUser` | `UserSuspended` | `RecordUserChangesHandler` | platform | `user.suspended` | the system admin |
| `ReactivateUser` | `UserReactivated` | `RecordUserChangesHandler` | platform | `user.reactivated` | the system admin |
| `RenameUser` | `UserRenamed`, only when a name changed | `RecordUserChangesHandler` | platform | `user.renamed` | the system admin |
| `RemoveUserMembership` | `UserMembershipRemoved` | `RecordMemberRemovedHandler` | tenant | `member.removed`; the target is the user, by id; the details hold the role the user had | `Platform` |
| `DeleteUser` | `UserDeleted`, only for the first deletion | `RecordUserChangesHandler` | platform | `user.deleted` | the system admin |
| `ChangePreferences` | `UserPreferencesChanged`, only when a value changed | `RecordUserChangesHandler` | platform | `user.preferences_changed`; the target is the user, by id; no details: the record holds no value | the user, for themselves |
| `AcceptInvitation` | `MemberJoined` | `RecordMemberJoinedHandler` | tenant | `member.joined` | the user who joined |
| `InviteMember` | `MemberInvited` | `RecordMemberInvitedHandler` | tenant | `member.invited`; the target is the invitation, by id; the details hold the role and the replaced invitation's id | the member who invited |
| `RevokeInvitation` | `InvitationRevoked`, only for a pending invitation | `RecordInvitationRevokedHandler` | tenant | `invitation.revoked`; the target is the invitation, by id | the member who revoked it |
| `ChangeMemberRole` | `MemberRoleChanged`, only when the role changed | `RecordMemberRoleChangedHandler` | tenant | `member.role_changed`; the target is the member, by id; the details hold the old and the new role | the member who changed it |
| `RemoveMember` | `MemberRemoved` | `RecordMemberRemovedHandler` | tenant | `member.removed`; the target is the member, by id; the details hold the role the member had | the member who removed them |

### Reading the audit logs

An Owner or an Admin reads the tenant's log, "Activity" on the screen (`tenant.audit.read`). A system admin reads the platform's log (`admin.audit.read`). The endpoints are in `Audit.Api`, in the audience folders (N7): `Tenant/AuditLog.cs` and `Admin/AuditLog.cs`.

| Message | Sent by | Handled by | Handler has a transaction |
| --- | --- | --- | --- |
| `ListAuditEntries` | `GET /v1/tenants/{tenantId}/audit-log` | Audit, `ListAuditEntriesHandler` | Yes, the tenant's (R1) |
| `ListPlatformAuditEntries` | `GET /v1/admin/audit-log` | Audit, `ListPlatformAuditEntriesHandler` | Yes; it declares neither a tenant nor a user (R11) |
| `GetAuditEntryNames` | both endpoints, after the page | Audit, `GetAuditEntryNamesHandler` (`IUserNames`) | Yes, ControlPlane's |

- The lists page by a cursor, newest first, by `occurred_at` and then `id`, both descending (catalog, K5). The cursor holds the last item's pair, so a record written between two requests moves no item to another page. A query selects one more record than the limit; when it finds it, the answer's `nextCursor` points after the last item, else it is null.
- The cursor is base64url of a version, the moment in UTC ticks, the id and a 4-byte SHA-256 checksum of those. A cursor that does not decode, or whose checksum does not match, answers 400 `paging.cursor_invalid`. The checksum is not a secret: a forged cursor only moves the page within what row level security lets the caller read.
- The names come from ControlPlane, in one call for each page (K3, API composition). The endpoint sends two queries, because Wolverine gives a handler the transaction of one DbContext and the records and the names live in two modules (W1). Wolverine builds `UserNames` in the second handler, on ControlPlane's DbContext, so it is public and sealed: the one contract implementation that is not internal (section 2, W9; `ContractsShapeTests`).
- An actor is a user, the platform or, in a record written before step 20i, the identity provider (`kind`). A record written before K3 has no actor kind and reads as the platform's (K3).
- `displayName` and a user target's `name` hold only a person's real name, else null. The API writes no label: the web speaks English, Turkish and Arabic, and a client could not tell a label from a real name. A client names the platform and the identity provider from `kind`, and a user without a name from `deleted`, as it does for `ListMembers`.
- A user actor and a user target carry `deleted`: true for a user a system admin deleted (A8). Any other actor or target has `deleted` null. An id the catalog does not know has no name and `deleted` false. The answer holds no email address.
- The permission catalogue is in `ControlPlane.Contracts`, which `Audit.Api` does not reference (section 2). The endpoints state the names again; `EndpointAccessTests` fails for an endpoint whose permission the catalogue does not hold.
- Tests: `ListAuditEntriesTests`, `ListPlatformAuditEntriesTests`, `AuditActorIdMigrationTests`, `CursorRequestTests`, `ListContractTests`.

Source: Richardson, *Microservices Patterns*, "API composition". Kleppmann, Chapter 3 (index order). PostgreSQL documentation, "Row Constructor Comparison".

## 7. Side tools and configuration

Only email goes into the first template. Hangfire, SignalR and cache code do not.

- Notifications does not know the email service directly. Our interface is between them, and the service is an adapter. Source: Freeman and Pryce, GOOS Chapter 8.
- Each email carries an idempotency key. Example: `invite/` and the invitation id. The same key does not produce a second email.
- Tests send no real email. They use a hand-written fake adapter.
- The messaging tool's own dispatcher runs the outbox. A scheduler is not used for this.

Three helper tools are in scope. The OpenAPI document and Scalar are on only in the development environment. OpenTelemetry sends logs, metrics and traces over OTLP. The target address comes from configuration. If there is no address, no data is sent. A test checks the trace id rule: the id on the request is the same in the handler of the event that the request caused.

The build writes the OpenAPI document to openapi/v1.json, and the file is committed. A change to the API then shows in the pull request. Every client's types are generated from it (N11).

### Configuration

1. Each module has its own configuration section. A module does not read another module's configuration.
2. Configuration is validated at startup. If a required setting is missing, the application does not start and names the missing key.
3. A value that changes between environments comes from an environment variable.
4. Passwords and keys are not written in `appsettings.json`.

Source: Twelve-Factor, Factor III. Nygard, Chapter 5 (Fail Fast).

### Container image

The API has one container image, built from `apps/api/Dockerfile`. Every process runs from it, and only the configuration and the command change. `Host:Role` selects the web or the worker role. The migration is a command of the same image: `docker run <image> migrate`. The web process listens on port 8080 for the API and on port 8081, the management port, for the health endpoints. A worker listens only on port 8081. Both listen on all interfaces (section 8). The process runs as the base image's non-root user. The image holds no secret, no connection string and no `appsettings.Development.json`. The runtime base image is chiseled: it has no shell and no package manager. It runs in invariant globalization mode, because the code needs no ICU and no time zone data: the API keeps a user's time zone as text and never converts a time (section 6, "Language and time zone"). The image has no Kerberos library, so the API turns GSSAPI encryption off on each database connection (`docs/configuration.md`). CI builds the image and scans it. Then it runs the migration, the web and the worker against PostgreSQL. It checks that each process is ready and that no process tries to load the Kerberos library.

Source: Twelve-Factor, Factors V, VII, IX and XII. CIS Docker Benchmark 4.1. Humble and Farley, *Continuous Delivery*, Chapter 5.

## 8. Resilience and security

### Resilience

| Situation | Rule | Source |
| --- | --- | --- |
| An external service does not answer | Each external call has a time limit | Nygard, Chapter 5 (Timeouts) |
| An external service is down for a long time | The gap between tries grows. When the limit is reached, the step fails for good: the saga compensates, or goes to "needs attention" | Nygard, Chapter 5 |
| The same request arrives twice | The host's idempotency layer claims the key and saves the handler result in the business transaction. A second request replays it (I1 to I7) | Stripe, "Idempotent requests". Brandur Leach, atomic phases |
| A pod is shutting down | The pod takes no new work and finishes the work it has | Twelve-Factor, Factor IX |
| The health of a pod is not known | There are two check endpoints: "I am up" and "I can reach the database" | Industry practice |
| Someone looks for a failure | The trace id stays the same in the log, in the event and in the handler | Nygard, Chapter 8. Twelve-Factor, Factor XI |

### Security

| Attack | Protection | OWASP |
| --- | --- | --- |
| Another tenant's id is put in the path | Membership check and RLS | API1 |
| A forged or expired token | Signature, expiry and issuer are verified on each request. Only a session token is accepted: a token without the session's id (`sid`), such as a JWT template's token, is refused | API2 |
| Extra fields are added to a request | An endpoint reads only the defined fields | API3 |
| Too many requests | A rate limit for each user, kept in each pod. A caller without a user is limited by its client address. Accepting an invitation has a stricter limit of its own. Lists are paged and the page size has a limit | API4 |
| A tenant user calls a system endpoint | The system door checks the staff list | API5 |
| A suspended user, or a suspended or closed tenant | A suspended user gets 403 `user.suspended` on every `me` and tenant endpoint, checked in one place on each request (A9). A member of a suspended tenant gets 403 `tenant.suspended`, and a member of a closed one 403 `tenant.closed`, at the tenant door and again under the tenant's lock in every tenant command (K6); a non-member still gets 404, so a stranger learns nothing (T3). Only the admin side sees the reason of a suspension. A system admin cannot be suspended | API1, API5 |
| Internal structure is read from an error message | The error response has one shape and returns no internal detail. A 401 does not say why the token failed: every 401 is `auth.unauthenticated` with the same body, and keeps `WWW-Authenticate: Bearer`. A request the server refuses while it reads it, such as a body over its endpoint's size limit, keeps the server's status (413, 400 or 408); any other exception is a 500 | API8 |
| A browser request from another site | Only permitted origins can call | API8 |

All business endpoints start with `/v1`. The only exception is the two health endpoints: `/health/live` and `/health/ready`. These endpoints serve the infrastructure, need no identity and return only "healthy" or "unhealthy". Neither health endpoint is reachable from outside. The outside sees no health information at all.

### Two ports

| Port | Default | Serves | Reached from outside |
| --- | --- | --- | --- |
| Public | 8080 in the container image, which sets `ASPNETCORE_HTTP_PORTS=8080`. The process reads `ASPNETCORE_URLS`, else `ASPNETCORE_HTTP_PORTS` and `ASPNETCORE_HTTPS_PORTS`, else uses `http://localhost:5000` | The business endpoints (`/v1/...`) and, in Development, the OpenAPI document and Scalar | Yes, through the platform's domain |
| Management | 8081 (`Host:ManagementPort`) | `/health/live` and `/health/ready` only | No. Only the platform's health check and Kubernetes probes reach it |

- The web process listens on both ports. A worker listens only on the management port: it has no business endpoints.
- A health path on the public port answers 404. Any other path on the management port answers 404.
- The process chooses by the port that the connection arrived on (`HttpContext.Connection.LocalPort`), never by the `Host` header. A client can put any port in the `Host` header.
- The health endpoints are not endpoint routes. A branch of the pipeline on the management port answers them, before authentication runs.
- Kestrel ignores `ASPNETCORE_URLS` and `ASPNETCORE_HTTP_PORTS` when it is given an endpoint in code. So the host adds the management port to the public addresses that those settings name.
- The process does not start when the management port is a public port.
- The platform routes public traffic to 8080 only. On Railway, `PORT` is 8081 for the health check, and the domain targets 8080. On Kubernetes, both probes use port 8081, and the Service exposes only 8080 (`setup.md`).

Source: Microsoft Learn, "Health checks in ASP.NET Core", "Require host": restrict the health endpoints to a port for monitoring services, and check `HttpContext.Connection.LocalPort`, because the `Host` header can be spoofed. Microsoft Learn, "Configure endpoints for the ASP.NET Core Kestrel web server". Railway docs, "Healthchecks", "Configure the healthcheck port". Spring Boot's `management.server.port` is the same practice.

## 9. Testing

The test is written first. Tests run with real PostgreSQL. We do not fake our own database.

| Kind | Example | Runs with |
| --- | --- | --- |
| Unit | "A cancelled tenant cannot be activated" | Code only |
| Integration | "An Acme user cannot read a Globex row" | Real PostgreSQL |
| Architecture | "No module references the inside of another module", "there is no endpoint without an access state" | Compiled code |
| End to end | A system admin creates a tenant. The Owner gets an email, accepts it and sees the tenant in the list. Another user gets 404 | The full system, real PostgreSQL |

1. The end-to-end test is written first. Source: Freeman and Pryce, GOOS Chapters 4 and 5.
2. Real PostgreSQL runs in a container. An in-memory fake database is not used. Source: Khorikov, Chapter 10.
3. Tests connect with the application account. Migrations run with the separate account. If not, RLS tests have no meaning.
4. Only external services are faked: the identity service and email. A fake is a hand-written implementation of our interface. Source: Khorikov, Chapter 9. Time is faked with FakeTimeProvider. Tokens in tests are signed with a test key the host is given.
5. Each test creates its own tenant.
6. A test host on the shared database of `Api.IntegrationTests` runs no idempotency cleanup. The cleanup deletes every expired row on its database, by the clock of its host. A test that moves a fake clock past the retention would delete the idempotency rows of other tests. `ApiFactory` and `PipelineHost` remove the cleanup from a host on the shared database (`SharedDatabaseCleanupTests`). A test of the cleanup runs on a database of its own. Source: Meszaros, *xUnit Test Patterns*, Chapter 6 (Interacting Tests) and Chapter 18 (Shared Fixture).
7. A run of `Api.IntegrationTests` holds at most 70 percent of the server's `max_connections` at once. The server allows 1800, so the budget is 1260. Production pool settings do not change. Source: Nygard, *Release It!* (2nd ed.), Chapter 5 (Bulkheads); xUnit.net documentation, "Running Tests in Parallel".
   - The worst case is 22 hosts × 41 connections + 36 = 938. It is within the budget.
   - 41 is the most connections one host can hold. A host has two pools, one for requests and one for messages. `Database.ForHost` caps each pool of a test host at 20 connections. A test can ask for fewer. The start check keeps 1 more connection in Npgsql's global pool until the host stops. When a host stops, Wolverine releases each durable local queue on a connection of its own. Without the cap, the messages pool grew to one connection per queue, and one host opened 76 connections.
   - A cap below Wolverine's need stalls its handlers. A cap of 5 did. 20 is far above the 2 to 3 connections that an idle host holds.
   - The cap also limits how many connections a run opens and closes. The side that closes a TCP connection keeps its local port in TIME_WAIT, for 30 s on macOS. Without the cap, a run closed about 48,000 connections in 165 s and used all 16,384 local ports of macOS. Linux reuses such ports on loopback (`tcp_tw_reuse`), so CI did not show the failure. `HostConnectionTests` fails when a stopping host has opened more than 40 connections in its life. Source: Stevens, *TCP/IP Illustrated, Volume 1* (2nd ed.), Chapter 13 (TIME_WAIT).
   - 36 is the most that the fixture and the other connections held.
   - 12 threads run 12 test classes at once. `xunit.runner.json` pins `maxParallelThreads`, so the bound holds on any machine.
   - A class runs one test at a time. A class whose tests run several hosts at once adds its extra hosts. `TwoWorkerTests` and `UnknownMessageTests` run 3 hosts, 2 extra each. `HostEndpointTests`, `HostRoleTests`, `RequestLoggingTests`, `TraceIdTests`, `TenantTransactionTests` and `CorsTests` run 2 hosts, 1 extra each. So 12 + 10 = 22 hosts.
   - 36 and the number of hosts are measured. A new class with several hosts changes the sum.
   - Every connection of the tests names its owner in `application_name`. A host gives its kind and its test class, as `ApiFactory:CreateTenantTests`. A direct call of the fixture gives `fixture`. Wolverine names its advisory lock connection itself, as `wolverine-advisory-lock`.
   - The assembly fixture counts the connections every 100 ms. It fails the run over the budget and names the owners at the peak.
8. A hung run of `Api.IntegrationTests` leaves its evidence behind. Nobody has to be there when it hangs. Source: Microsoft Learn, "Microsoft.Testing.Platform crash and hang dumps"; xUnit.net documentation, "Configuration with xunit.runner.json" (`longRunningTestSeconds`).
   - The options are in `TestingPlatformCommandLineArguments` of the test project. Under Microsoft.Testing.Platform, xUnit ignores `longRunningTestSeconds` in `xunit.runner.json`.
   - xUnit writes a line for each test that runs for 60 s or more into the diagnostic log of the run (`--long-running 60`). The slowest test, `HostMemoryTests`, takes about 20 s.
   - When no test ends for 3 minutes, the hang dump extension writes a heap dump of the test process and a list of the tests in progress, then ends the run (`--hangdump-timeout 3m`).
   - The files go to the results directory: `bin/Debug/net10.0/TestResults` of the test project on a local run, and `apps/api/TestResults` in CI. When the test step fails, or a hang runs into the job's time limit, CI keeps the folder for 7 days as the artifact `api-test-results`.
   - Read a dump with `dotnet-dump analyze <file>`: `clrstack -all` gives every thread, `dumpasync` gives every waiting async method.
9. A tracked session waits up to 30 s for the messages a test caused, on `ApiFactory` and on `PipelineHost` (`ApiFactory.TrackingTimeout`). Wolverine's default is 5 s. A message that fails for good runs 4 times, with pauses of 100 ms, 500 ms and 2 s, before it reaches the dead letter queue. Under load each run can take a second, so 5 s is too short.
10. A test that leaves a message scheduled runs on a database of its own. Every Wolverine node runs the scheduled messages of its database when their time comes, whichever node scheduled them. In Balanced mode Wolverine 6.47 starts the scheduled jobs on every node, with or without the durability agent (`WolverineRuntime.startAgentsAsync`). On the shared database, a host of another test runs the message, with its own fakes, inside its own tracked session. Source: Meszaros, *xUnit Test Patterns*, Chapter 6 (Interacting Tests) and Chapter 18 (Shared Fixture).
   - Example: `InvitationDeliveryTests` schedules an invitation's next try a minute away. A minute later, a host of `TenantCreationTests` ran it while its email service was down, and the test saw two dead letters instead of one.
   - The two tests of `InvitationDeliveryTests` with a pause of a minute run on a database of their own.
   - The timeouts that the tenant creation process schedules come after 10 minutes, 10 minutes and 2 hours. A run ends before them.
   - `ExpireSignUp` comes at an invitation's expiry, 7 days after the clock of the test. A run ends before it. A test whose clock lies in the past makes it due at once: it runs in that test's own host, and the module's clock, which says the lifetime is not over, makes it withdraw nothing.
11. A test host loads the committed handler code, as production does (W10). A host that adds a handler or a handler policy of a test has chains that the committed code does not hold. It generates and compiles its handler code at runtime (`GenerateHandlerCodeAtRuntime`), and its test class carries `[RuntimeHandlerCode]`; the host does not start without it. These hosts do: `PipelineHost`, the hosts of `HostRoleTests`, `TwoWorkers` and `IdempotencyTests`, and the new worker of `RollingDeploy`. Only `Api.IntegrationTests` has `WolverineFx.RuntimeCompilation`. Source: Wolverine, "Working with Code Generation".
   - On the committed code, a frame that a test policy adds never runs, because Wolverine finds the type by its name. `ApiFactory` does not start such a host (`CommittedHandlerCodeTests`). A test handler that a host adds with `Discovery.IncludeType` stops the start too (`MissingPreBuiltTypesException`). A handler in an assembly that a host adds with `Discovery.IncludeAssembly` is not found at all, because Wolverine reads the committed registry. So `PipelineHost` generates its code at runtime.
   - The fakes of the tests do not change the committed code. The code takes the email channel from the message's scope and the clock as a singleton. The code of each kind of `ApiFactory` host was written once and compared with the committed files.
   - A host on runtime code keeps its compiled code and the compiler's memory until the process ends: 26 to 41 MB on average over 150 hosts, and more for the first hosts of a process (143 MB each in `HostMemoryTests`). A host on the committed code keeps about 4 MB, because timers of Wolverine and ASP.NET Core keep a stopped host reachable. `HostMemoryTests` fails when a host on the committed code keeps 10 MB or more. It runs alone, after the other tests (`RunsAlone`).
   - CI runs the tests with `scripts/ci/test-api.sh`. It prints the peak resident memory of each test process and stops a process that passes 11 GB, so a regression fails with its reason. All of `Api.IntegrationTests` in one process peaked at 8.9 GB in one run and at 11.0 GB in the next, on a runner with 16 GB. So the script runs the tests in two parts, one after the other, until a stopped host leaves no memory behind: the classes marked `[RuntimeHandlerCode]`, and every other test.

Unit test standard:

- Each test kind is in its own project: .UnitTests, .IntegrationTests, .EndToEndTests and .ArchitectureTests (N9).
- Each project carries a `TestClassification` trait.
- The structure is Arrange, Act, Assert.
- A test has no `if` and no loop.
- The name pattern is `Operation_Scenario_ExpectedOutcome`. `Operation` is the name of the work, not the method name. Example: `ActivateTenant_WhenCancelled_IsRejected`.

An architecture test enforces the trait, the name pattern and the rule against branches and loops. The branch check does not see loops written with LINQ. Use a theory for several cases.

An architecture test fails when a module DbContext has model changes without a migration (`PendingModelChangesTests`). Source: Microsoft, "Managing Migrations" (EF Core), check for pending model changes.

Module integration tests call a handler with the module's DbContext on real PostgreSQL, inside a transaction the test opens. The Wolverine pipeline is tested in Api.IntegrationTests. The spike in apps/api/spikes is outside the test standard.

The order for a fix is: first a red test that checks the rule, then the fix.

## 10. Names and places

One concept has one name in the code, the route, the table and the documents. A name says what a thing is. A folder says what the code is about and who uses it. Where a rule names a test, the test checks the part of the rule that the note names. The rest of the rule is checked in review.

Sources: Evans, *Domain-Driven Design*, Chapter 2 (Ubiquitous Language). Richards and Ford, *Fundamentals of Software Architecture*, "Component-Based Thinking" (domain partitioning). Robert C. Martin, "Screaming Architecture". Microsoft Learn, "Route handlers in Minimal API apps", route groups. OWASP API Security Top 10 2023, API5. Richardson, *Microservices Patterns*, Chapter 4 (command and reply).

### Names

| # | Rule | Source |
| --- | --- | --- |
| N1 | A feature has one root word. An action (endpoint, command, handler) uses its verb form: `CreateTenant`, `CreateTenantHandler`. A record (saga, table) uses its noun form: `TenantCreationSaga`, `tenant_creation_sagas`. The documents use the noun form: "the tenant creation process". A configuration key keeps its name when a type is renamed | Evans, Chapter 2 |
| N2 | A class that derives from Wolverine's `Saga` ends with `Saga`. Its table ends with `_sagas`. Every table name is plural. Test of the saga class and table names: `SagaNameTests` | Evans, Chapter 2 |
| N3 | A saga handles three kinds of message only: the replies to its steps (`...Reply`), its own timeouts (`...TimedOut`) and Wolverine's `Fault<T>`. It handles no public event. Only a saga handles a `...Reply`. A reply is named after the work it answers: `ActivateTenantReply` answers `ActivateTenant`. The name makes S13 visible. Test of both sentences: `SagaMessageTests` | Richardson, Chapter 4 |
| N4 | A module's schema is the module's name in snake case: `control_plane`, `audit`. The host's schema is `api`, and its DbContext is `ApiDbContext`. A module's DbContext is `{Module}DbContext`. A name that describes a concept, not the module, stays: `ITenantCatalog` is the tenant catalog. Test of the schema name: `NamesAndPlacesTests` | Evans, Chapter 2. Richards and Ford, "Component-Based Thinking" |
| N5 | An audience is who calls an endpoint. Its route prefix and its permission prefix are the same word, and the module folder of its endpoints has that name (table below). The host defines every audience's route group in one file, `Api/Routing/RouteGroups.cs`. A new audience adds one group there. Example: `MapMe` in `RouteGroups.cs`, `/v1/me`, `ControlPlane.Api/Me/`. Test of the endpoint folder against the route prefix: `EndpointPlaceTests`. Test that no other file under `src` calls `MapGroup`: `RouteGroupPlaceTests` | Microsoft Learn, route groups. OWASP API5 |
| N6 | A permission is `audience.resource.action`, in lower snake case. The audience is `admin` or `tenant`. The action is coarse: `read`, `create`, `update`, `delete`, or a business verb. `read` covers a list and a single record. The `me` audience has no permissions: being signed in is enough, and the user reaches only their own data. The system pool holds `admin.tenants.create`, `admin.tenants.read`, `admin.tenants.update`, `admin.tenants.suspend`, `admin.tenants.reactivate`, `admin.tenants.close`, `admin.tenants.reopen`, `admin.users.read`, `admin.users.suspend`, `admin.users.reactivate`, `admin.users.update`, `admin.users.delete` and `admin.audit.read`; `suspend`, `reactivate`, `close` and `reopen` are business verbs. The tenant pool holds `tenant.members.read` and `tenant.profile.read`, which every built-in role holds, and `tenant.members.update`, `tenant.members.delete`, `tenant.invitations.create`, `tenant.invitations.read`, `tenant.invitations.delete` and `tenant.audit.read`, which only the Owner and Admin roles hold. Test of the format and the two audiences: `NamesAndPlacesTests` | OWASP API5 |

| Audience | Route prefix | Module folder | Host method in `Api/Routing/RouteGroups.cs` | Permission prefix |
| --- | --- | --- | --- | --- |
| System admin | `/v1/admin` | `Admin/` | `MapAdmin` | `admin.` |
| The signed-in user, acting for themselves | `/v1/me` | `Me/` | `MapMe` | none |
| A tenant member | `/v1/tenants/{tenantId}` | `Tenant/` | `MapTenant` | `tenant.` |

The word for the system admin audience is `Admin`, not `System`. A folder named `System` makes the namespace `...Api.System`, and that namespace hides .NET's `System` namespace. The role keeps its name, "system admin", and it still passes the system door (A6). `Me` is a Visual Basic keyword, so CA1716 does not check namespaces (`.editorconfig`). It still checks types and members.

### Places

| # | Rule | Source |
| --- | --- | --- |
| N7 | In a module's `Api` project, the endpoints sit in the audience folder, one file for each resource: `Admin/Tenants.cs`, `Admin/Users.cs`, `Me/MyProfile.cs` (the signed-in user's own profile, `GetMe`, and their preferences, `ChangePreferences`), `Me/MyTenants.cs`, `Me/Invitations.cs`, `Tenant/Profile.cs` (the tenant of the route itself, `GetMyTenant`, permission `tenant.profile.read`), `Tenant/Members.cs`, `Tenant/Invitations.cs` (the tenant's invitations, which its Owners and Admins manage), and in Audit `Tenant/AuditLog.cs` and `Admin/AuditLog.cs`. The module entry point (`ControlPlaneModule.cs`) calls the files. An endpoint is a method with the action's name, the same name as its handler in `Application`. `DeleteUser` sends the steps of a user's deletion and then the command of its name (section 6, "User deletion"). The request and response types sit at the end of the file that uses them. A request is named by its action (`{Action}Request`), a response by its resource (`{Resource}Response`). A second shape of the same resource carries its audience in the name (`MyTenantResponse`). A response that two audiences answer stays in the file of the first one: `MyTenantResponse` sits in `Me/MyTenants.cs`, and `Tenant/Profile.cs` answers it too. A list's sort enum (`ListTenantsSort`) sits next to its query in `Application`, because the query carries it; each member's name in camel case is a value of `sort`, the name of the response field it sorts by. A list's filter takes the status enum of its response, or an enum at the end of its endpoint's file. `MemberRole`, at the end of `Tenant/Members.cs`, is the one enum of a built-in role: the role filter of the member and the invitation lists, and the role of `InviteMemberRequest` and `ChangeMemberRoleRequest`. A body gives a role by its exact name only (`ExactNameEnumConverter`, section 2): a number, a list of names, a name with spaces or a name in another case answers 400 `request.invalid`, as any body the endpoint cannot read (`ChangeMemberRoleTests` and `InviteMemberTests` check the 400, `ProblemDetailsTests` the code). A response keeps a role as a string, because a custom role comes later (A3). `Language`, in `SharedKernel`, is the one enum of a language in every request and response (section 2). The status of a response is an enum of its own, at the end of the same file, with only the values that response can carry: `TenantResponseStatus` (provisioning, active, suspended, cancelled, closed), `MyTenantResponseStatus` (active, suspended, closed), `UserResponseStatus` (active, suspended, deleted). The enum is named after its response, so it hides no Domain type such as `TenantStatus` (N10). Each value is one lowercase word in JSON (`JsonStringEnumMemberName`). The database and ControlPlane keep their own names (`Active`); the module's `Api` project maps them in one place (`ResponseStatus`) when it builds each response, so a saved idempotent result replays to the same answer. Every request and response name is unique in the API, because the OpenAPI document builds its schema names from the type names. Tests of the audience folder (`EndpointPlaceTests`), of the unique names (`NamesAndPlacesTests`) and of each status enum in the document (`OpenApiContractTests`) | Martin, "Screaming Architecture" |
| N8 | The folders of `Application` follow the work. The folders of `Domain` follow the aggregate. The folders of `Infrastructure` follow the external system: `Persistence/`, `Clerk/`, `Email/`. A feature with more than one file gets its own folder, named by N10. A feature with one file stays in its resource folder. Code that several features of one module use goes into a folder named by what it is: `Ports/`, `Persistence/`. No project has a folder named `Helpers`, `Utils`, `Utilities`, `Common` or `Misc`. Code that several modules use lives only in `SharedKernel` or `Tenancy`. Test of the folder names: `NamesAndPlacesTests` | Richards and Ford, "Component-Based Thinking". Martin, "Screaming Architecture" |
| N9 | A test project that tests one module sits in the module's folder. A test project that tests a shared project sits next to it. Tests of the whole system (host, architecture, end to end) sit in `apps/api/tests/`. A test project is named `{Project}.{Kind}Tests`, where Kind is Unit, Integration, EndToEnd or Architecture. Inside a test project the folders follow the project it tests. A test class is named `{Feature}Tests`. A test that checks several features goes into the folder of the feature it checks last. Helper files go into `Fixtures/`. Test of the project name: `NamesAndPlacesTests` | Section 9. Industry practice |
| N10 | A feature with more than one file always gets a folder in the noun form of its root, never the action name: `Tenants/TenantCreation/` holds `CreateTenant`, and `Invitations/InvitationEmail/` holds `SendInvitationEmailHandler`. The reason: the folder becomes a namespace, and a folder named after the action hides the command type of the same name (CS0118). For the same reason, no folder has the name of a type its project declares or uses from a project it references. Test of that last sentence: `NamesAndPlacesTests` | Cwalina, Barton, Abrams, *Framework Design Guidelines*, "Names of Namespaces" |
| N11 | The API contract is code-first. The C# request and response types (N7) are the source. The build writes `openapi/v1.json` from them, and the types of every client are generated from that file. Every endpoint in the document has an operation id equal to its name, a PascalCase word: `CreateTenant`, `ListMembers`. A status is the enum its response declares (N7), and .NET writes it into the document. The host adds what the endpoints do not state. `Api/Hosting/OpenApiContract.cs` adds the operation ids, the path parameters that a route group puts in a path (a `:guid` constraint gives format uuid, as `{tenantId:guid}`; any other is a plain string), the values of a list's `sort` and filters (K5 in the catalog), the values of an enum whose schema the generator leaves without values, which is an enum with a converter of its own (`ExactNameEnumConverter`: `Language`, K8 in the catalog, and `MemberRole`), and a reference to the enum's schema in each nullable field of it, which the generator leaves without a schema (such a schema has no `type`, as every enum that .NET writes), and the `code`, `errors`, `traceId` and `tenantIds` of the `ProblemDetails` schema (`tenantIds` only on an error that names tenants, K5 in the catalog): `code` is required and is the schema `ErrorCode`, the codes of every static `...ErrorCodes` class the host reaches: `OpenApiContract` reads, by reflection, the host's assembly and each assembly it references, directly or not, that references `SharedKernel`, so also each module's Domain project, which the host does not reference at compile time (ReferenceRuleTests). It reads the codes and nothing else, and each field of `errors` lists the schema `FieldErrorCode`. `Api/ErrorHandling/ResultResponses.cs` declares that one `ProblemDetails` schema on every error answer, and 401 and 429 on every operation. An endpoint that creates a resource declares 201 (`Produces`), and the host answers its success with 201 in place of 200 (`ResultEndpointFilter`): `CreateTenant`, `InviteMember`. Every endpoint that takes `Idempotency-Key` declares 201 (I1). The transformers change the document only, never the JSON on the wire. Every error answer is Problem Details with a `code`, in the form `area.reason`, that an `...ErrorCodes` class declares. `Api/ErrorHandling/Problems.cs` is the one file that writes it: an answer from an `Error` carries the error's code, and any other answer the host writes, such as a 401, a 403 without a refusal, the rate limiter's 429, a body the endpoint cannot read, a route that does not match or a 500, gets the code of its status. A validation answer is `request.validation_failed`; its `errors` name each field by its command property in camel case, which is the request's JSON name, and list the field code that each failed rule states (`required`, `email_invalid`, `too_long`, `time_zone_invalid`). `detail` stays English, for developers and logs. A client shows its own text from `code`, never from `detail`. Every client uses `@repo/api-client` (`packages/api-client`). No client writes an API type by hand. Tests: `OpenApiContractTests`; `ErrorCodeTests` (only a static readonly field of an `...ErrorCodes` class declares a code, each code once; only `Problems.cs` writes the code member, and only it and `OpenApiContract.cs` name it; every validator rule states a field code); `ProblemDetailsTests`; `OpenApiDocumentFileTests` (the committed document is current); the `node` job in `ci.yml` (the committed `src/schema.ts` is the output of `pnpm api:generate`) | Microsoft Learn, "Customize OpenAPI documents" (transformers). OpenAPI Specification 3.1, "Path Templating". RFC 9457, Problem Details. Hunt and Thomas, *The Pragmatic Programmer*, Topic 9 (DRY) |

The ControlPlane module after these rules:

| Project | Folders |
| --- | --- |
| `ControlPlane.Api` | `Admin/`, `Me/`, `Tenant/`, `Authentication/` (the signed-in user's id), `ControlPlaneModule.cs` |
| `ControlPlane.Application` | `Tenants/TenantCreation/` (command, handler, validator, saga, steps, messages, settings, alarm), `Tenants/ListMyTenants.cs`, `Tenants/ListTenants.cs`, `Tenants/GetMyTenant.cs`, `Tenants/GetTenant.cs`, `Tenants/RenameTenant.cs`, `Tenants/SuspendTenant.cs`, `Tenants/ReactivateTenant.cs`, `Tenants/CloseTenant.cs`, `Tenants/ReopenTenant.cs`, `Tenants/TenantSettings.cs` (the reopen period), `Tenants/TenantDetails.cs` (the admin's shape of a tenant, which eight features use), `Invitations/` (with `Invitations/SignUp/`, the identity provider's invitation), `Members/`, `Users/UserDeletion/` (the steps of a user's deletion), `Users/RenameUser.cs`, `Users/ListUsers.cs`, `Users/GetMe.cs`, `Users/ChangePreferences.cs`, `Users/SuspendUser.cs`, `Users/ReactivateUser.cs`, `Users/UserDetails.cs` (the admin's shape of a user, which three features use), `Ports/`, `Errors.cs`, `IdentityProviderRetries.cs` (the retries of the calls to the identity provider) |
| `ControlPlane.Domain` | One folder for each aggregate. `PermissionPools.cs` and `ControlPlaneErrorCodes.cs`, which several aggregates use, sit at the root |
| `ControlPlane.Infrastructure` | `Persistence/` (DbContext, its design-time factory, migrations, `TenantCatalog`, `TenantDirectory`, `UserTenants`, `UserCatalog`, `UserDirectory`, `UserNames`, `SystemAdminDirectory`, `SystemAdminSettings`, `ListQueries` (the search pattern and sort direction of the lists)), `Clerk/` (the Backend API), `ControlPlaneInfrastructure.cs` |

## Deviations from the books

These deviations were discussed openly and approved.

| Deviation | The way in the books | Reason |
| --- | --- | --- |
| The tenant is in the API path, not in the token | Golding, Chapter 6: the tenant comes in the token | The identity provider stays authentication only. A cancelled membership takes effect immediately. The Azure guidance accepts this way |
| The isolation unit is a schema, not a database | Richardson: a database for each service | The rule is for microservices. It is adapted to a monolith. The change back is cheap |
| There is one shared outbox | Richardson, Chapter 3: the outbox is in the service's own database | An outbox row is a delivery record, not business data. The wolverine tables are not under RLS. They hold message bodies, invitation links included, until Wolverine removes handled messages. |
| Each module is five projects | Khononov, Chapter 10 and Fowler, PoEAA Chapter 2: a simple structure for a simple module | One pattern was requested. The cost is some nearly empty projects |
| The test name pattern is `Operation_Scenario_ExpectedOutcome` | Khorikov, Chapter 3: a plain sentence | Common in .NET. `Operation` is the name of the work |
| The `Api` and `Application` projects see Wolverine types | Khononov, Chapter 8 (Ports and Adapters): business logic does not see the infrastructure tool, a port is between them | The only reason for our own interface was "what if the tool changes". There is no concrete limitation. `Domain` and `Contracts` stay clean |
| Handler, saga, message and DbContext types are public | Ousterhout, Chapter 5: a module hides its internal detail | Wolverine's handler code is in another assembly than the handler (W9). Project references and the architecture test protect the module boundary |
| The invitation link goes to Notifications inside the message | OWASP, Forgot Password Cheat Sheet: a token is stored only as a hash | The link cannot be produced again later. The secret travels in ActivateTenant, OpenOwnerSignUp, SignUpOpened and OwnerInvitationReady, and for a member's invitation in OpenMemberSignUp, SignUpOpened and MemberInvitationReady, and stays in the messaging tables until Wolverine removes handled messages. The identity provider keeps the accept link, with the code, as the redirect URL of its invitation (the maintainer's decision in step 20i); its invitation is withdrawn when ours ends. Protection: the verified email of the person who accepts must be the same as the invited address, the link is single use and expires, and an owner invitation whose email does not go out is cancelled. A member invitation whose email does not go out stays pending; a new invitation to the address replaces it, and an Owner or an Admin can revoke it. If a strict audit requires it, change to a signed token |
| The rate limit is kept in each pod | OWASP API4: a limit for each user | A shared store (Redis or the database) is infrastructure the first template does not need. The real limit is the setting times the number of pods |
| In Development, email without an email service is written to the log, link included | OWASP Logging Cheat Sheet: secrets are not logged | Local development and the smoke run need the link without an email account. Outside Development the application does not start without the email service |
| The API image has no `HEALTHCHECK` | CIS Docker Benchmark 4.6: add a `HEALTHCHECK` instruction to the image | The chiseled image has no shell and no `curl` to run a check. The platform checks `/health/live` and `/health/ready` over HTTP on the management port: its own HTTP health check or Kubernetes probes (`setup.md`). Docker ignores `HEALTHCHECK` under Kubernetes |

## Deferred

These parts are not in the first template. Each one comes in when its written condition is true.

| What | Entry condition |
| --- | --- |
| Hangfire | The first recurring job tied to a clock appears and the messaging tool cannot do it |
| SignalR | In-app notifications come into scope |
| Cache code | There is a measured slowness. The rules are ready |
| Circuit breaker | A request that a user waits for goes directly to an external service |
| A separate database account for ControlPlane | A product or audit requirement asks for it |
| Slug in addresses | The frontend wants the tenant name in the address. The slug field is ready. The API path continues to use the id |
| Read model | A report appears that needs a join across modules |
| Features | Custom roles, tenant deletion, support access, in-app notifications, a second system admin |
| The tenant creation state ("needs attention") in the admin's tenant list | The admin app needs it. The saga record belongs to the tenant (RLS), and the list reads across tenants, so the state needs a copy outside RLS or a read that declares each tenant in turn |
| Member counts in the admin's tenant list (R12) | The admin app needs them. They come from ControlPlane's own summary data |

## Open items

None.
