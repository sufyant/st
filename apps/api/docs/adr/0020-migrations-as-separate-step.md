# 0020. Migrations run as a separate step

- Status: Proposed
- Date: 2026-10-04

## Context

Several pods start at the same time, and the application role must not own tables (0018).

## Decision

- Migrations run as a separate, one-off step, never on application start.
- Order: the bootstrap script (0018), then the migrations.
- **The step is a command of the host:** `dotnet Api.dll migrate`. It builds the same service container as the application, runs every registered module migrator (`IModuleMigrator`, 0048) over `ConnectionStrings:Migrations` (the owner, 0019), and exits without serving requests. Without that setting it fails.
- **Each module owns its migrations**, in its Infrastructure project, with its own history table (`__ef_migrations_history`) in its own schema (0008).
- **Writing a migration:** migrations are written with the repository's local `dotnet-ef` tool:

  ```text
  dotnet ef migrations add <Name> --project src/Modules/X/X.Infrastructure --startup-project src/Modules/X/X.Infrastructure
  ```

  Each module has a design-time DbContext factory, so the tool never starts the host or connects. Analyzers treat the generated migration files as generated code.

## Alternatives considered

- **Migrate on application start.** Pods race each other and the application would need owner rights.
- **EF Core migration bundles per module.** One executable per module, with the order kept in the deployment script.
- **A separate migrator project.** One more project that references every module's Infrastructure, which the reference table (0006) would have to allow.

## Consequences

- Deployment needs a migration step before the new version starts (CI/CD itself is out of scope, 0045).
- Migrations must be compatible with the version still running during rollout.
- The same image serves requests and runs migrations; only the command differs.
