# Architecture: multi-tenant SaaS starter template (API)

Last updated: 2026-10-07

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
6. The template is platform independent. Provider detail (example: Neon) does not go into the architecture. It goes into the setup notes.
7. Infrastructure without a use does not go in. A part is added only if it catches a failure that no other control catches.

### Scope of the first template

The first template runs one flow from start to end: tenant creation.

| In scope | Reason |
| --- | --- |
| `POST /v1/system/tenants` | The work itself |
| The first system admin comes from configuration | The first person who creates a tenant cannot get on the staff list in another way |
| Tenant onboarding process (saga, four steps) | The approved flow |
| One handler that sends the invitation email (Notifications) | Step four of the process |
| One handler that writes the "tenant created" record (Audit) | The only use of the Audit module |
| The endpoint that accepts an invitation | Without it, the first Owner cannot exist |
| `GET /v1/me/tenants` | The user sees their tenants after sign-in |
| One read endpoint behind the tenant door. Example: `GET /v1/tenants/{tenantId}/members` | It exercises the tenant door, membership, RLS and the permission check |

Out of scope: member invitation by an Owner, custom roles and role assignment, member removal, tenant suspension and deletion, support access, in-app notifications, adding a second system admin, the tenant list for the system admin.

## 1. The big picture

The system is one code base and one build output. This output runs as two process types: web and worker.

| Decision | Source |
| --- | --- |
| The system is one deployment unit. The code is divided by business area | Richards and Ford, FSA 2nd ed., Chapter 11 |
| Each module is a bounded context | Evans, Chapter 14. Khononov, Chapter 3 |
| One artifact, two process types. Web serves HTTP requests. Worker runs background work. A setting selects the role | Twelve-Factor, Factor VIII. Nygard, Chapter 5 (Bulkheads) |
| One PostgreSQL schema for each module. No foreign keys and no joins across modules | Evans, Chapter 14. Richardson, "Database per Service" (adapted to a monolith) |
| The host contains no business rule. It only connects the modules | Industry practice (composition root) |

In Kubernetes this is two Deployments and one image. The requirement is a separate process, not a separate machine.

## 2. Module boundary and structure

Each module is five projects. Another module sees only the `Contracts` project.

| Project | Content | Depends on |
| --- | --- | --- |
| `X.Contracts` | Interfaces, DTOs, event types. No domain types | Nothing |
| `X.Api` | HTTP endpoints | Application, Contracts |
| `X.Application` | Use case handlers, contract implementations, sagas | Domain, Contracts |
| `X.Domain` | Business rules | Nothing |
| `X.Infrastructure` | EF Core, adapters for external services | Application, Domain |

- Two controls enforce the boundary: project references (compilation) and architecture tests. Source: Ford, Parsons, Kua, Chapter 2.
- The `Api` project cannot reach the `DbContext`. Source: Evans, Chapter 4.
- A read across modules is a plain method call. The interface is in `A.Contracts`. Its implementation is an `internal sealed` class inside A. Source: Gamma et al., Chapter 4 (Facade). Evans, Chapter 14 (Open Host Service).
- The shared kernel stays small and carries no business concept. Source: Evans, Chapter 14.
- Test projects are next to their module. System-wide tests (architecture, end to end) stay in the top `tests` folder.

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

Requirements 3 and 5 do not have a test yet. A two-pod test is part of the fix plan.

### Handlers and pipeline

Wolverine is the dispatcher. An endpoint sends a command or a query through `IMessageBus`. A handler is a plain class. Shared behaviour is Wolverine middleware. Source: Fowler, PoEAA Chapter 9 (Service Layer). Hohpe and Woolf, Chapter 3 (Pipes and Filters) and Chapter 10 (Message Dispatcher).

| Order | Command chain | Query chain |
| --- | --- | --- |
| 1 | HTTP: authentication, tenant membership, rate limit, authorization (ASP.NET Core) | HTTP: authentication, tenant membership, rate limit, authorization (ASP.NET Core) |
| 2 | Logging and tracing | Logging and tracing |
| 3 | The transaction opens, the tenant is declared | The transaction opens, the tenant is declared |
| 4 | Input validation | Input validation |
| 5 | Handler | Cache (only on marked queries) |
| 6 | Save, commit, then outbox dispatch | Handler |

Authorization runs once, in ASP.NET Core, before the endpoint sends the message. A refused request opens no transaction. A message from a queue has no caller and is not authorized again.

- Wolverine opens and closes the transaction. We set the rule: each handler runs in its own transaction.
- A handler that calls an external service uses no DbContext, so no transaction is open during the call. It returns its result as a message. Source: Nygard, Chapter 5 (Integration Points).
- A domain event is handled inside the module and in the same transaction. An integration event leaves the module through the outbox. Source: Vernon, Chapter 8.
- HTTP endpoints are plain ASP.NET minimal APIs. The tool's own endpoint model (Wolverine.Http) is not used.

### Wolverine setup rules

These ten rules come from a spike. The spike is in `apps/api/spikes/WolverineRlsSpike`. The T numbers are the tests in the spike.

| # | Rule | Reason | Evidence |
| --- | --- | --- | --- |
| W1 | Wolverine's EF Core middleware opens and closes the transaction. There is no hand-written transaction code | The saga record, the business data and the outgoing message go into one transaction (O1, S6) | T3, T4 |
| W2 | The module's DbContext reads the tenant from the message context. An EF Core transaction interceptor runs `set_config('app.tenant_id', ..., true)` when the transaction starts | R4, R5 | T1, T2, T5, T6 |
| W3 | A tenant endpoint uses `InvokeForTenantAsync`. The tenant goes to the following messages automatically. A message without a tenant carries the value `*DEFAULT*`. The interceptor does not treat this value as a tenant | Requirement 7, R5 | T3, T6 |
| W4 | `MultipleHandlerBehavior.Separated` is on | O3 | T8 |
| W5 | Messages wait in durable queues, and the message store is in the shared `wolverine` schema. In the role `all`, a message goes to a durable local queue (`UseDurableLocalQueues`). In the roles `web` and `worker`, every message goes through the outbox to one PostgreSQL queue in the same schema (Wolverine's PostgreSQL transport). Only a worker listens to that queue, and it hands an event with several handlers on to durable local queues, one for each handler (W4). A web host runs no durability agent (`DurabilityAgentEnabled = false`): outbox and inbox recovery run in a worker | O1, O5, Requirement 5, section 1 | T3, `HostRoleTests` |
| W6 | A saga derives from Wolverine's `Saga` class. Its record is stored with EF Core in the module's own schema. The `Version` property is mapped as a concurrency token. There is a retry policy for `SagaConcurrencyException` | S3, S8 | T7, T9 |
| W7 | A business rule rejection returns before any data changes: in a `Validate` or `Before` method. A failure after a change is an exception | Wolverine also commits a handler that returns a failed `Result` | T10a, T10b |
| W8 | The transaction middleware is first in the Wolverine chain. Validation runs inside the transaction. Authorization runs before, in ASP.NET Core | This is Wolverine's behaviour. A rejected request opens an empty transaction and writes no data | Generated handler code |
| W9 | Handler, saga, message and DbContext types are `public` | Wolverine compiles handler code in a separate assembly | Compile errors CS0051 and CS0122 |
| W10 | The `WolverineFx.RuntimeCompilation` package is necessary | Handler code is generated at startup. Pre-generated code was not tried | The host did not start without the package |

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
| S11 | Until the process ends, the related record shows the "provisioning" state | Richardson, Chapter 4 (semantic lock) |
| S12 | The saga class is unit tested without a host. The saga does not call external services. It only decides and returns messages | Khorikov, Chapter 7 |

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
| T2 | The tenant id is not read from the body, the query string or a header. Commands have no tenant field. A handler gets the tenant from the tenant context | Golding, Chapter 7 |
| T3 | If there is no membership, the response is 404 | OWASP API Top 10, API1 |
| T4 | `GET /v1/me/tenants` runs without a tenant | Industry practice |
| T5 | Provider staff are not tenant members. Their door is the `/v1/system/...` path | Golding, Chapter 2 |
| T6 | The token carries only the user identity. No tenant, role or permission comes from Clerk | Fixed requirement: Clerk is authentication only |
| T7 | Membership is verified on each request in one place. No tenant endpoint can go around this filter | OWASP API Top 10, API1 |

A tenant has two descriptive fields. `name` is free text, cannot be empty, has a maximum of 100 characters and does not have to be unique. `slug` is unique and is given when the tenant is created. `GET /v1/me/tenants` returns both. The path, authorization and isolation use only the id.

### Row Level Security

| # | Rule | Source |
| --- | --- | --- |
| R1 | Each table that belongs to a tenant has `tenant_id`. RLS is enabled and forced | Golding, Chapters 8 and 9 |
| R2 | The application account is not the table owner and cannot bypass RLS | Golding, Chapter 9. OWASP (least privilege) |
| R3 | Migrations run with a separate account. That account is not used at run time | Twelve-Factor, Factor XII |
| R4 | The tenant is declared at the start of each transaction. A connection-level setting is not permitted. A transaction declares a tenant or a user, never both. | PostgreSQL connection pool behaviour |
| R5 | If the tenant is not declared, a query returns no data | Nygard, Chapter 5 (Fail Fast) |
| R6 | Tables without a tenant are on an explicit list. A test checks every table | Ford, Parsons, Kua, Chapter 2 |
| R7 | There are only two database accounts: migration and application. Running code has no path that bypasses RLS | Golding, Chapter 9 |
| R8 | A background handler gets the tenant from the message and declares it in the same way | Requirement 7 |
| R9 | Each tenant table is tested with real PostgreSQL: read, update, insert for another tenant | Khorikov, Chapter 10 |
| R10 | At startup the application checks its own account. If the account is a table owner or can bypass RLS, the application does not start | Nygard, Chapter 5 (Fail Fast) |
| R11 | The membership table has one more policy, `own_memberships`: `FOR SELECT` only, on `user_id` = the declared user (`app.user_id`, set in the transaction like the tenant). It serves `GET /v1/me/tenants`. Any policy other than `tenant_isolation` must be on an explicit list, and a test fails on any other | Postgres combines permissive policies with OR. A write policy would widen access |
| R12 | The system admin sees tenant and member counts from ControlPlane's own summary data | Golding, Chapter 2 |

Work across tenants goes through the tenant list and declares each tenant in turn. RLS does not protect a cache or data that leaves the database. An EF Core query filter is not added as a second layer. RLS is the single source of truth.

## 5. Identity and authorization

The code checks permissions, not roles. Provider staff enter through a separate door.

| # | Rule | Source |
| --- | --- | --- |
| A1 | Authorization is permission based. The code asks for a permission, not for a role name | OWASP API Top 10, API5. Golding, Chapter 6 |
| A2 | The built-in roles are Owner, Admin and Member. They are the same in each tenant and cannot be changed | Industry practice |
| A3 | A tenant builds its own role (custom role) from the fixed permission list. This feature is not in the first template | Industry practice |
| A4 | A system admin is not a tenant member. The staff list is in ControlPlane. It has its own permission list | Golding, Chapter 2 |
| A5 | Each endpoint carries one of three explicit states: public, signed-in only, requires a permission. An endpoint without a state breaks the architecture test. Each endpoint on the tenant path requires a permission | OWASP API Top 10, API5 |
| A6 | The system door requires a second factor. The rule is in ControlPlane. The host only translates the identity provider's field into a neutral value. Tenant users are not affected | OWASP ASVS, item 4.3.1 |

Tenant users and staff sign in with the same Clerk instance. Our tables decide which door a person can use. Terms: "system admin" (Golding), "built-in roles" and "custom roles".

## 6. Modules and onboarding

There are three modules. A system admin creates the tenant and the first Owner comes by invitation.

| Module | What it holds |
| --- | --- |
| ControlPlane | Tenant record, system admins, users, memberships, roles and permissions, invitations, tenant onboarding |
| Notifications | It hears an event and sends a message to a person. In the first template, only the invitation email |
| Audit | It hears an event and writes the record of "who did what and when" |

Source: Golding, Chapter 2 (control plane). Khononov, Chapter 3 (when you are not sure, keep the boundary wide).

### Tenant onboarding process

The process is a saga. Four steps run in order.

1. ControlPlane writes two rows in one transaction: the tenant ("provisioning") and the Owner invitation. Compensatable.
2. The identity service is told "this email can sign up". The invitation link is created here. Compensatable.
3. The tenant becomes "active". Pivot.
4. Notifications sends the invitation email. Retried until it succeeds.

Onboarding messages:

| Message | Sent by | Handled by | Handler has a transaction |
| --- | --- | --- | --- |
| `StartTenantOnboarding` | `POST /v1/system/tenants` | ControlPlane, `StartTenantOnboardingHandler` (step 1, starts the saga) | Yes |
| `RegisterOwnerWithIdentityProvider` | Step 1 | ControlPlane, `RegisterOwnerWithIdentityProviderHandler` (step 2, calls Clerk) | No |
| `RegistrationTimedOut` | Step 1, scheduled | ControlPlane, `TenantOnboarding` saga | Yes |
| `OwnerRegistered` | Step 2 | ControlPlane, `TenantOnboarding` saga | Yes |
| `ActivateTenant` | Saga | ControlPlane, `ActivateTenantHandler` (step 3, pivot) | Yes |
| `TenantActivated` | Step 3 | Audit, `RecordTenantCreatedHandler`; ControlPlane, `TenantOnboarding` saga | Yes, each its own |
| `OwnerInvitationReady` | Step 3 | Notifications, `SendOwnerInvitationHandler` (step 4, calls the email service) | No |
| `InvitationEmailTimedOut` | Saga, scheduled | ControlPlane, `TenantOnboarding` saga | Yes |
| `InvitationEmailSent` | Step 4 | ControlPlane, `TenantOnboarding` saga | Yes |
| `CancelTenant` | Saga | ControlPlane, `CancelTenantHandler` (compensation) | Yes |
| `TenantCancelled` | `CancelTenantHandler` | ControlPlane, `TenantOnboarding` saga | Yes |
| `RevokeOwnerRegistration` | Saga | ControlPlane, `RevokeOwnerRegistrationHandler` (compensation, calls Clerk) | No |
| `CancelInvitation` | Saga | ControlPlane, `CancelInvitationHandler` (compensation) | Yes |
| `RaiseOnboardingAlarm` | Saga | ControlPlane, `OnboardingAlarmHandler` | No |
| `Fault<T>` of a step or a compensation | Wolverine, when the message goes to the dead letter queue | ControlPlane, `TenantOnboarding` saga | Yes |

- If step 2 or step 3 fails, the tenant is cancelled. A reason code is stored on the tenant and logged. No endpoint shows it yet: the tenant list for the system admin is deferred.
- If step 4 always fails, the tenant stays active and the process goes to the "needs attention" state. The invitation is cancelled.
- The invitation link travels to Notifications inside the message. See the deviations list.
- The invitation link carries one invitation code: the tenant id and a secret, `<tenantId>.<secret>`. Only the hash of the secret is stored. Accepting declares the tenant from the code, then finds the invitation by the hash in that tenant. A wrong tenant, a wrong secret and a malformed code all answer 404. This is the only place where a request names its tenant outside the path. The secret gives the right, not the tenant id.
- Accepting an invitation needs two things: the secret in the code, and a verified email address of the signed-in user that is the same as the invited address. The verified addresses come from the identity provider's server, not from the request.
- The email handler is in `Notifications.Application`. The event type is in `ControlPlane.Contracts`. ControlPlane does not know how to send email.
- Invitation expiry is checked when the invitation is read. There is no nightly job.
- The first system admin comes from an email address in configuration. The application writes it only while the staff list is empty.

Source: Golding, Chapter 4. Richardson, Chapter 4.

## 7. Side tools and configuration

Only email goes into the first template. Hangfire, SignalR and cache code do not.

- Notifications does not know the email service directly. Our interface is between them, and the service is an adapter. Source: Freeman and Pryce, GOOS Chapter 8.
- Each email carries an idempotency key. Example: `invite/` and the invitation id. The same key does not produce a second email.
- Tests send no real email. They use a hand-written fake adapter.
- The messaging tool's own dispatcher runs the outbox. A scheduler is not used for this.

Three helper tools are in scope. The OpenAPI document and Scalar are on only in the development environment. OpenTelemetry sends logs, metrics and traces over OTLP. The target address comes from configuration. If there is no address, no data is sent. A test checks the trace id rule: the id on the request is the same in the handler of the event that the request caused.

### Configuration

1. Each module has its own configuration section. A module does not read another module's configuration.
2. Configuration is validated at startup. If a required setting is missing, the application does not start and names the missing key.
3. A value that changes between environments comes from an environment variable.
4. Passwords and keys are not written in `appsettings.json`.

Source: Twelve-Factor, Factor III. Nygard, Chapter 5 (Fail Fast).

## 8. Resilience and security

### Resilience

| Situation | Rule | Source |
| --- | --- | --- |
| An external service does not answer | Each external call has a time limit | Nygard, Chapter 5 (Timeouts) |
| An external service is down for a long time | The gap between tries grows. When the limit is reached, the process goes to "needs attention" | Nygard, Chapter 5 |
| The same request arrives twice | The tenant creation request carries an idempotency key. The second request does not create a new tenant | Hohpe and Woolf, Chapter 10 |
| A pod is shutting down | The pod takes no new work and finishes the work it has | Twelve-Factor, Factor IX |
| The health of a pod is not known | There are two check endpoints: "I am up" and "I can reach the database" | Industry practice |
| Someone looks for a failure | The trace id stays the same in the log, in the event and in the handler | Nygard, Chapter 8. Twelve-Factor, Factor XI |

### Security

| Attack | Protection | OWASP |
| --- | --- | --- |
| Another tenant's id is put in the path | Membership check and RLS | API1 |
| A forged or expired token | Signature, expiry and issuer are verified on each request | API2 |
| Extra fields are added to a request | An endpoint reads only the defined fields | API3 |
| Too many requests | A rate limit for each user. Lists are paged and the page size has a limit | API4 |
| A tenant user calls a system endpoint | The system door checks the staff list | API5 |
| Internal structure is read from an error message | The error response has one shape and returns no internal detail | API8 |
| A browser request from another site | Only permitted origins can call | API8 |

All business endpoints start with `/v1`. The only exception is the two health endpoints: `/health/live` and `/health/ready`. These endpoints serve the infrastructure, need no identity and return only "healthy" or "unhealthy". No detail is given to the outside.

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
4. Only external services are faked: the identity service and email. A fake is a hand-written implementation of our interface. Source: Khorikov, Chapter 9.
5. Each test creates its own tenant.

Unit test standard:

- Each test kind is in its own project (`.UnitTests`, `.IntegrationTests`).
- Each project carries a `TestClassification` trait.
- The structure is Arrange, Act, Assert.
- A test has no `if` and no loop.
- The name pattern is `Operation_Scenario_ExpectedOutcome`. `Operation` is the name of the work, not the method name. Example: `ActivateTenant_WhenCancelled_IsRejected`.

The order for a fix is: first a red test that checks the rule, then the fix.

## Deviations from the books

These deviations were discussed openly and approved.

| Deviation | The way in the books | Reason |
| --- | --- | --- |
| The tenant is in the API path, not in the token | Golding, Chapter 6: the tenant comes in the token | Clerk stays authentication only. A cancelled membership takes effect immediately. The Azure guidance accepts this way |
| The isolation unit is a schema, not a database | Richardson: a database for each service | The rule is for microservices. It is adapted to a monolith. The change back is cheap |
| There is one shared outbox | Richardson, Chapter 3: the outbox is in the service's own database | An outbox row is a delivery record, not business data |
| Each module is five projects | Khononov, Chapter 10 and Fowler, PoEAA Chapter 2: a simple structure for a simple module | One pattern was requested. The cost is some nearly empty projects |
| The test name pattern is `Operation_Scenario_ExpectedOutcome` | Khorikov, Chapter 3: a plain sentence | Common in .NET. `Operation` is the name of the work |
| The `Api` and `Application` projects see Wolverine types | Khononov, Chapter 8 (Ports and Adapters): business logic does not see the infrastructure tool, a port is between them | The only reason for our own interface was "what if the tool changes". There is no concrete limitation. `Domain` and `Contracts` stay clean |
| Handler, saga, message and DbContext types are public | Ousterhout, Chapter 5: a module hides its internal detail | Wolverine compiles handler code in a separate assembly. Project references and the architecture test protect the module boundary |
| The invitation link goes to Notifications inside the message | OWASP, Forgot Password Cheat Sheet: a token is stored only as a hash | The link cannot be produced again later. Protection: the verified email of the person who accepts must be the same as the invited address, the link is single use and expires, an invitation whose message goes to the dead letter queue is cancelled. If a strict audit requires it, change to a signed token |

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
| Pre-generated handler code | Startup time becomes a problem or the RuntimeCompilation package is not wanted in production. Try it first |
| Provider setup notes | Example: on Neon the application account is created with SQL. In Clerk the second factor is enabled and the first system admin enrolls a device. `ConnectionStrings:Messaging` is needed when `ConnectionStrings:Database` goes through a pooler in transaction mode (example: Neon's pooled endpoint), because Wolverine's message store holds session-level advisory locks; it then names a direct or session-mode connection. These go into the setup list, not into the architecture |
| Features | Member invitation, custom roles, member removal, tenant suspension and deletion, support access, in-app notifications, a second system admin, the tenant list for the system admin |

## Open items

- Requirements 3 and 5 for the messaging tool have no evidence yet. The two-pod test is written in the fix plan.
- The licenses of transitive test packages are not verified yet.
