# 0046. API build and tests run as Turborepo tasks

- Status: Proposed
- Date: 2026-10-04

## Context

The repository is a pnpm and Turborepo monorepo. The root `build` script ran `dotnet restore` on the API solution before `turbo build`, so the API was restored but not built, and `pnpm test` did not run the .NET tests at all.

## Decision

- `apps/api` gets a `package.json`, which makes it a workspace package.
- Its `build` and `test` scripts run `dotnet build` and `dotnet test` on `Api.slnx`; Turborepo runs them as the package's `build` and `test` tasks.
- The root `build` script no longer runs `dotnet restore`; `dotnet build` restores on its own.
- Turborepo does not cache the API `build` task. Its outputs (`bin/`, `obj/`) contain machine-specific absolute paths, so a cache restored on another machine would be wrong; `dotnet build`'s own incremental build keeps repeated builds fast.

## Alternatives considered

- **Call `dotnet` from the root scripts.** Two orchestrators side by side; the API would sit outside Turborepo's task graph.
- **Keep only the restore in the root script.** The API is never built or tested by `pnpm build` and `pnpm test`.

## Consequences

- `pnpm build` and `pnpm test` cover the whole repository, the API included.
- Wherever these tasks run, the .NET SDK is needed, and Docker for tests that use Testcontainers (0044).
- Every `pnpm build` runs `dotnet build` on the API; a cache hit never skips it.
