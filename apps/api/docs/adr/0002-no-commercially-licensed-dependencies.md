# 0002. No commercially licensed dependencies

- Status: Proposed
- Date: 2026-10-04

## Context

Every product built on the template inherits its dependencies. MediatR, AutoMapper, MassTransit (v9 onward) and FluentAssertions have moved to commercial licences. A dependency that later requires a paid licence becomes a cost and a forced migration for every product at once.

## Decision

- The template uses no dependency that requires a commercial licence. MediatR, AutoMapper, MassTransit v9+ and FluentAssertions are excluded.
- Every new package gets a licence check before it is added. A package with architectural impact also needs an ADR.
- Packages flagged during setup are verified for licence and .NET 10 compatibility in the phase that introduces them (see To verify).

## Alternatives considered

- **Buy the licences.** A recurring cost carried by every product and a dependency on the vendor's future pricing.
- **Pin the last free versions.** No security fixes and an eventual forced migration anyway.

## Consequences

- Replacements are chosen elsewhere: Wolverine for mediator and messaging (0023), Shouldly for assertions (0044); mapping is written by hand.
- Adding a package takes a deliberate step.

## To verify

- The maintained NetArchTest fork: licence and .NET 10 compatibility. Verified in Phase 1, recorded in 0044.
- The Hangfire PostgreSQL storage package: licence and .NET 10 compatibility. Verified in Phase 6, recorded in 0027.

## Checked when added

- Phase 4:
  - `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Options.ConfigurationExtensions` (10.0.12): MIT, part of .NET.
  - `FluentValidation` 12.0.0: Apache 2.0, the version WolverineFx.FluentValidation already brings, now referenced by `ControlPlane.Application` for its validators.
  - No Clerk SDK: the adapter calls Clerk's Backend API over HTTP.
- Phase 5:
  - `WolverineFx.Postgresql` 6.45.0 (MIT), Wolverine's PostgreSQL message storage (0024). It brings `WolverineFx.RDBMS` 6.45.0, `Weasel.Core` and `Weasel.Postgresql` 9.38.0, `JasperFx.Events`, and `Patched.DistributedLock.Core` and `Patched.DistributedLock.Postgres` (all MIT); `Npgsql.NetTopologySuite` 9.0.4 (PostgreSQL License); and `NetTopologySuite` 2.5.0 and `NetTopologySuite.IO.PostGis` 2.1.0 (BSD-3-Clause). All of it runs on .NET 10 with the Npgsql 10 the application already uses.
  - `WolverineFx`, already in the host, is now referenced by `ControlPlane.Application` too, for the failure policies its handlers declare (0023).
- Phase 6:
  - `Microsoft.Extensions.Http.Resilience` 10.10.0 (MIT, part of .NET) for outbound clients (0041). It brings `Microsoft.Extensions.Resilience`, `Microsoft.Extensions.Telemetry` and their abstractions (MIT), and `Polly.Core`, `Polly.Extensions` and `Polly.RateLimiting` (BSD-3-Clause).
  - Hangfire (0027): `Hangfire.Core`, `Hangfire.NetCore` and `Hangfire.AspNetCore` 1.8.25, and `Hangfire.PostgreSql` 1.21.1, all LGPL-3.0. That is an open-source licence, not a commercial one: Hangfire OÜ sells a commercial licence only for distributing private modifications of Hangfire itself, which the template does not do. LGPL obliges a product that ships Hangfire to let users replace that library; referencing the unmodified NuGet packages meets it. They bring `Cronos` 0.11.0 (MIT) and `Dapper` 2.0.123 and `Dapper.AOT` 1.0.48 (Apache-2.0).
  - `Newtonsoft.Json` 13.0.4 (MIT), referenced directly wherever Hangfire is: Hangfire.Core accepts 11.0.1, which has a known high-severity vulnerability and fails the build's audit.
  - `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.12 (MIT, part of ASP.NET Core) for the backplane (0037). It brings `StackExchange.Redis` 2.7.27 and `Pipelines.Sockets.Unofficial` 2.2.8 (MIT).
  - Tests only: `Microsoft.AspNetCore.SignalR.Client` 10.0.12 and `Testcontainers.Redis` 4.15.0 (MIT).
  - `FluentValidation` and `WolverineFx` are now referenced by `Notifications.Application` as well, for its validators and failure policies (0023).
