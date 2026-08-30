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
  admin        Next.js               platform back-office, separate host
  marketing    Next.js               deploys to Vercel
  mobile       Expo                  ships via EAS Build / EAS Update
packages/
  shared       types, zod schemas
  api-client   generated from the API's OpenAPI schema
docs/          cross-cutting notes and decisions
scripts/       repo-wide tooling
```

`docs/` and `scripts/` hold what spans subtrees. Anything one app owns belongs
under that app, not here.

## Subtree Guidance

Each subtree owns its build commands, stack details, and conventions. Before
running commands or changing code under one, load and follow its `AGENTS.md`:

| Touching | Read first |
|---|---|
| `apps/api/*` | `apps/api/AGENTS.md` |
| `apps/web/*` | `apps/web/AGENTS.md` |
| `apps/admin/*` | `apps/admin/AGENTS.md` |
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
- **`apps/admin` is a separate identity domain.** Platform admins authenticate
  against their own directory, never the customer one — a customer credential
  must not be able to reach admin surface at all, not merely fail a check. Admin
  routes do not go through tenant resolution; the target tenant is an explicit
  parameter on the request. Never add an "if admin, skip tenancy" branch to the
  normal request path. There is no admin user table — identity lives in the
  provider; only admin *actions* are persisted, as an audit trail.
- **The identity provider supplies identity only — membership is ours.** Clerk
  answers "who is this person" and nothing else. Which tenants a person may enter
  is a row in our own catalog, keyed by the provider's user id. Two consequences,
  both deliberate:
  - **Do not enable Clerk Organizations.** Not `clerk enable orgs`, not
    `<OrganizationSwitcher />`, not org claims in the JWT. Tooling will keep
    offering it — Clerk's own setup skill ends by suggesting it. Decline. Using
    it would move tenant membership into the provider, which is the one thing
    that makes the provider expensive to leave, and starts a per-organization
    meter we have no reason to pay.
  - Invite and membership screens are ours to build. That cost is accepted.
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
