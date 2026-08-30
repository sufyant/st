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
  shared       types, zod schemas, Intl formatters — every app
  api-client   generated from the API's OpenAPI schema — every app
  tokens       design tokens as plain data — every app, mobile included
  i18n         message catalogs as plain JSON — every app
  ui           shadcn components — web, admin, marketing only (needs a DOM)
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

Only `apps/web` is scaffolded. The rest land with their app.

## Cross-Cutting Rules

These govern the boundaries *between* subtrees. Rules that live inside one
subtree belong in that subtree's `AGENTS.md`, not here.

### Boundaries

- **Share data and contracts, not runtime.** `apps/mobile` is React Native and
  has no DOM, so it can never consume `packages/ui`. What crosses every app is
  plain data — tokens, message catalogs, types, the generated client. Runtime is
  shared only among apps on the same platform, which is why `packages/ui` serves
  the three Next.js apps and nothing else. When a new package is proposed, decide
  which of the two it is before writing any of it.
- **The API is the contract.** `packages/api-client` is generated from the API's
  OpenAPI schema. To change what the frontends see, change the .NET contract and
  regenerate. Never hand-edit generated output, and never work around a stale
  client by patching the consumer.
- **Dependency direction is one-way.** `apps/*` may import from `packages/*`.
  `packages/*` must never import from `apps/*`, and apps must never import each
  other. Shared logic moves into `packages/shared`.
- **Each app deploys independently.** A change under one app must not require
  redeploying another. CI triggers on paths — but a change to a package must
  trigger every app that consumes it, so path filters follow the dependency
  graph, not just directory names.
- **Never call the API by hand.** Every request goes through `packages/api-client`.
  A hand-written `fetch` outlives the contract it was written against without
  saying so — which is the exact failure the generated client exists to prevent.
- **One validation schema, shared with the API.** Client-side validation uses the
  zod schema in `packages/shared`, never a second hand-written check. A duplicate
  validator drifts from the server's rules quietly, and the first evidence of the
  drift is bad data already committed.

### Presentation

- **UI lives in three layers.** Design tokens in `packages/tokens`; primitives
  with no domain knowledge (the shadcn output — Button, Input, Dialog) in
  `packages/ui`; product components that understand the domain (TenantSwitcher,
  PropertyCard) inside the app that shows them. A product component moves into
  `packages/ui` only when a *second* app actually needs it — never in
  anticipation. This is what keeps "each app deploys independently" honest:
  primitives change rarely, and the components that change daily never leave
  their app.
- **`packages/ui` must not depend on `packages/shared`.** It is the layer test in
  executable form: if a component needs a domain type, it is a product component
  and belongs in an app.
- **shadcn is not a dependency.** The CLI copies source files that we then own.
  Components are added into `packages/ui`, not into an app, and are edited freely
  afterward.
- **Tokens are platform-neutral data.** A color exists in exactly one file,
  `packages/tokens/src/theme.ts`, authored as hex. The web's CSS variables are
  generated from it and committed; native reads the same values as TypeScript.
  Never write a color into a component, a stylesheet, or the generated
  `tokens.css` — the last of those is silently discarded on the next
  regeneration, taking native out of step with web.
- **Contrast is measured, not judged.** `pnpm --filter @st/tokens check`
  regenerates the CSS to prove it is current, then fails if any surface and its
  paired foreground drops below WCAG AA in either theme. Run it after touching a
  color. Every accessible-looking palette that shipped inaccessible was approved
  by someone confident it looked fine.
- **Locale is a property of the person; theme is a property of the device.** A
  user's language lives in our database, because email rendered by the API must
  match what the app shows them. Theme preference lives in the browser or on the
  device — wanting dark on a phone and light on a desktop is correct behavior,
  not a bug.
- **Render on the server by default.** In the Next.js apps `"use client"` is a
  deliberate exception, applied to the smallest leaf that needs interactivity and
  never to a page or a layout. Read data in Server Components, server to server,
  and pass it down already narrowed. A browser-side call to the API needs a
  reason.
- **Only `apps/marketing` is indexable.** It owns `sitemap.ts`, `robots.ts`,
  canonical URLs, and per-locale `hreflang`. `apps/web` and `apps/admin` serve
  `Disallow: /` and never appear in a sitemap.

### Language

- **Translations are namespaced by owner.** Strings that genuinely repeat across
  apps live in `packages/i18n` (`common`, `errors`, `auth`). Everything else
  lives in the app that displays it. A single shared catalog would mean a copy fix
  in `web` forces a redeploy of `admin` and `marketing`.
- **The API returns error codes, never user-facing prose.** `Entity.ErrorName`
  maps onto the `errors.*` namespace, so `web` and `admin` translate the same code
  from the same catalog. An English sentence in an API response is a bug.
- **English is the only locale today, and nothing may assume it is the only one.**
  Adding a locale must be a config and translation change, never a refactor. So:
  no literal user-facing strings in components, and no date, number, or currency
  formatting outside the `Intl` helpers in `packages/shared`. Money is minor units
  in the database; formatting it is a presentation concern that needs both a
  locale and a currency.
- **`apps/admin` is not localized.** Its users are our own staff. English only, no
  locale routing, no catalogs.
- **`apps/web` carries no locale in the URL.** It sits behind auth and is not
  indexed; language comes from the user record. Only `apps/marketing` uses a path
  prefix, because SEO requires it.

### Identity

- **Hiding something in the interface is not authorization.** Not rendering a
  button because the user lacks a permission is a courtesy. The API must still
  reject the call. A feature whose permission check exists only in a client is
  unprotected, and nothing about the interface will reveal that.
- **Server-side rendering protects the bundle, not the data.** Keeping work on the
  server keeps secrets, tokens, and the API surface out of the browser — real
  benefits, and reason enough to default to it. It hides nothing that the browser
  is ultimately sent. If a user must not see a field, the API must not return it;
  proxying a call, renaming a route, or moving work server-side are not
  substitutes for an authorization rule.
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
- **The provider's id lives in exactly one column.** `users.external_auth_id`,
  next to `users.auth_provider`. No `clerk_*` column exists anywhere else, and a
  provider id is never a foreign key, a query-key fragment, a URL segment, or a
  cache key. Replacing the provider is then a backfill of one column, not a
  migration.
- **Only `lib/auth/**` may import the provider SDK.** Each app wraps the vendor
  behind a local module exporting our own session shape, typed in
  `packages/shared`. Enforce it with a lint rule so CI catches a stray import,
  rather than trusting the convention. There is deliberately no `packages/auth`:
  `web` and `admin` authenticate against different directories, and a shared auth
  module is precisely where an "if admin, skip tenancy" branch would eventually
  appear. The duplication is a security boundary, not an oversight.

### Scope

- **Scope discipline.** No Kubernetes, Elasticsearch, Redis, or event sourcing
  until a concrete problem demands one. v1 has no teams, billing, or onboarding.
  These are deliberate constraints. If a task seems to require breaking one, say
  so and wait for a decision — don't route around it.
- **Tests arrive with a trigger, not up front.** A runner installed before there
  is anything to verify is a file, not a safety net. The first pure function pulls
  in Vitest; the first working authenticated flow pulls in Playwright. Business
  logic lives in `apps/api` and is tested there — the frontend has less to unit
  test than it looks like.
- **The first end-to-end test is tenant isolation.** Whether a user in one tenant
  can ever observe another's data, including through a stale cache. That failure
  is invisible to every test written against a single tenant, which is why it does
  not get discovered on its own.
- **No build orchestrator yet.** pnpm workspace scripts are enough. Reach for
  Turborepo when CI time actually hurts or when a package change first fails to
  rebuild its consumers — not before.

## Environment

- Two hosts: Windows with PowerShell, and macOS with zsh. Either may be the one
  you are on, so check rather than assume. Don't take `rg`, `sed`, or
  `Select-String` for granted on either.
- Prefer a `pnpm` script over a raw shell command. It is the single interface that
  behaves identically on both hosts, which is why anything worth running twice
  belongs in `package.json` rather than in someone's terminal history.
- Anything committed under `scripts/` either runs on both hosts or ships as a pair
  sharing one name.
- Never hardcode a path separator. Build paths with the platform's own helper.
- Constrain searches by subtree or extension. No unbounded scans from the repo root.
- Text files are LF in the repo; `.ps1`/`.bat`/`.cmd` are the exception. This is
  what keeps the two hosts interchangeable — don't override it locally.

## Git

- Non-interactive commands only.
- **Never commit or push without explicit approval in that moment.** Make the
  change, show the diff, stop. Prior approval does not carry forward.
- Branch: `main`.
