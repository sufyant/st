# AGENTS.md

Monorepo for a multi-tenant product: API, web, marketing site, and mobile app.

## Overarching Mandate

1. Don't guess silently. State assumptions, surface confusion, and call out tradeoffs.
2. Minimum code that solves the problem. Nothing speculative.
3. Touch only what you must. Clean up only your own mess.
4. Define success criteria. Loop until verified.

## Repo Layout

```
apps/
  api          .NET Web API          deploys to Azure Container Apps
  web          Next.js               deploys to Azure Container Apps
  marketing    Next.js               deploys to Vercel
  mobile       Expo                  ships via EAS Build / EAS Update
packages/
  shared       types, zod schemas
  api-client   generated from the API's OpenAPI schema
```

## Subtree Guidance

Each subtree owns its build commands, stack details, and conventions. Before
running commands or changing code under one, load and follow its `AGENTS.md`:

| Touching | Read first |
|---|---|
| `apps/api/*` | `apps/api/AGENTS.md` |
| `apps/web/*` | `apps/web/AGENTS.md` |
| `apps/marketing/*` | `apps/marketing/AGENTS.md` |
| `apps/mobile/*` | `apps/mobile/AGENTS.md` |

Nothing is scaffolded yet — each file lands with its app.

## Cross-Cutting Rules

These govern the boundaries *between* subtrees. Rules that live inside one
subtree belong in that subtree's `AGENTS.md`, not here.

- **The API is the contract.** `packages/api-client` is generated from the API's
  OpenAPI schema. To change what the frontends see, change the .NET contract and
  regenerate. Never hand-edit generated output, and never work around a stale
  client by patching the consumer.
- **Dependency direction is one-way.** `apps/*` may import from `packages/*`.
  `packages/*` must never import from `apps/*`, and apps must never import each
  other. Shared logic moves into `packages/shared`.
- **Each app deploys independently.** A change under one app must not require
  redeploying another. CI triggers on paths.
- **Tenancy is resolved at the edge, once.** The tenant comes from the request
  token; no client sends a tenant identifier, and no application code threads one
  through. How this is enforced is `apps/api`'s business.
- **Scope discipline.** No Kubernetes, Elasticsearch, Redis, or event sourcing
  until a concrete problem demands one. v1 has no teams, billing, or onboarding.
  These are deliberate constraints. If a task seems to require breaking one, say
  so and wait for a decision — don't route around it.

## Environment

- Windows host, PowerShell primary. Prefer `Get-ChildItem -Recurse` and
  `Select-String`; don't assume `rg` is installed.
- Constrain searches by subtree or extension. No unbounded scans from the repo root.
- Text files are LF in the repo; `.ps1`/`.bat`/`.cmd` are the exception.

## Git

- Non-interactive commands only.
- **Never commit or push without explicit approval in that moment.** Make the
  change, show the diff, stop. Prior approval does not carry forward.
- Branch: `main`.
