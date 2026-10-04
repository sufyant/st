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
- The Hangfire PostgreSQL storage package: licence and .NET 10 compatibility (Phase 6, record in 0027).

## Checked when added

- Phase 4:
  - `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Options.ConfigurationExtensions` (10.0.12): MIT, part of .NET.
  - `FluentValidation` 12.0.0: Apache 2.0, the version WolverineFx.FluentValidation already brings, now referenced by `ControlPlane.Application` for its validators.
  - No Clerk SDK: the adapter calls Clerk's Backend API over HTTP.
