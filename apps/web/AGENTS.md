<!-- BEGIN:nextjs-agent-rules -->

# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` (resolved from this file's directory; in monorepos the `next` package may not be visible from the repo root) before writing any code. Heed deprecation notices.

This block is written and re-added by `next dev` — verify at `node_modules/next/dist/server/lib/generate-agent-files.js`. Removing it from a diff only re-creates the uncommitted change; committing it with your work keeps the tree clean.

<!-- END:nextjs-agent-rules -->

# apps/web

The customer-facing dashboard. Everything here sits behind authentication and is
scoped to one tenant. Read the root `AGENTS.md` first — the rules below only
cover what is specific to this app.

## Stack

Next.js 16 (App Router, Turbopack), React 19, Tailwind v4, Biome. Package name
`@st/web`. TypeScript path alias `@/*` points at `src/`.

## Commands

Run from the repo root so pnpm resolves the workspace:

```
pnpm --filter @st/web dev
pnpm --filter @st/web build
pnpm --filter @st/web typecheck
```

Linting and formatting are repo-wide, not per app: `pnpm lint` and `pnpm format`
from the root. There is one `biome.json`, at the root, and no package defines its
own — a second config is how two packages quietly stop agreeing on what formatted
code looks like.

Both commands are scoped to `apps` and `packages`. Skill packages under
`.claude/skills` ship other frameworks' example code, which fails our rules and
even trips our own `@clerk` import restriction. It is not our source and is not
linted.

Biome replaces both ESLint and Prettier. Do not add either.

## Where code goes

| What | Where |
|---|---|
| Routes, layouts, pages | `src/app/**` |
| Components that know our domain | `src/components/**` |
| Generic primitives (Button, Dialog) | `@st/ui` — never redefined here |
| Colors, spacing, typography | `@st/tokens` — never a hardcoded hex or px |
| Shared strings | `@st/i18n` |
| Strings only this app shows | `messages/**` |
| Auth | `src/lib/auth/**` — see below |

`@st/ui` and `@st/tokens` exist today. `@st/i18n`, `@st/shared` and
`@st/api-client` are where that code will live — do not invent a local substitute
for one, and do not import it before it is built.

A component in `src/components` that turns out to be generic does not move to
`@st/ui` until a second app needs it. Duplication is cheaper than a premature
shared abstraction that every app must then be redeployed for.

## Auth

`@clerk/*` may be imported **only** from `src/lib/auth/**`. This is enforced by
`noRestrictedImports` in the root `biome.json`, not by convention — an import
elsewhere fails `pnpm lint`. If it fires, widen the wrapper; never add an
exception.

There is no barrel export, deliberately: a single `@/lib/auth` would let server
code follow a client import into the browser bundle. Import the specific module.

| Need | Import from |
|---|---|
| Session in a Server Component | `@/lib/auth/server` |
| Session in a client component | `@/lib/auth/client` |
| Sign-in UI, user menu, provider | `@/lib/auth/components` |
| The `Session` type | `@/lib/auth/types` |

The wrapper renames what it re-exports — `AuthProvider`, `SignInForm`,
`UserMenu`. That is not decoration: Clerk Core 3 removed `<SignedIn>` and
`<SignedOut>` in a way that still typechecks and throws at runtime, and the
change stopped inside this folder.

This app authenticates against the **customer** Clerk instance. `apps/admin` uses
a different one. Never share an instance, a key, or a session between them.

Clerk's components wear our theme: `AuthProvider` passes `@clerk/ui`'s `shadcn`
theme, which reads the same CSS variables `@st/tokens` generates. The sign-in
form follows our palette and our light/dark switch without a second palette to
keep in sync. `@clerk/ui/themes/shadcn.css` is imported from this app's
`globals.css`, not from `@st/ui` — the design system must not know about an auth
vendor, and `apps/marketing` consumes `@st/ui` while never touching Clerk.

## Access is by invitation

There is no sign-up route and no sign-up component. The app cannot create an
account at all.

Invitations are ours to build, which the root AGENTS.md already accepts as the
price of keeping membership out of the provider. Our flow creates the identity
through Clerk's Backend API; the invited person then signs in. Nothing about that
needs a public form, so none exists — this is a closed door, not a hidden one.

**Our code being closed does not close the provider.** Public sign-up must also
be switched off on the Clerk instance, or its own hosted pages remain reachable.
The instance setting is the enforcement; everything in this repo is presentation.

Configuring it needs a linked application. `clerk doctor` reports the account is
already authenticated but this directory is "not linked — using the keyless
application", which covers fewer settings; `clerk config` therefore cannot reach
the sign-up restrictions. The unblocking step is `clerk link`, not another login.

Clerk supplies the user's identity and nothing else. Tenant membership, roles,
and permissions come from our API. Do not read tenant or role information out of
a Clerk claim even if one appears there.

## Rendering

Server Components are the default. `"use client"` is an exception that gets
justified case by case, and it goes on the smallest leaf that actually needs
interactivity — never on a page or a layout. Pushed up the tree, it ships that
whole subtree to the browser.

Props crossing into a client component are narrowed by hand. Passing a whole
user, tenant, or domain entity serializes every one of its fields into the
browser bundle, including the fields nobody meant to expose. Pass the three the
component renders.

## Data

Reads happen in Server Components, server to server, through `@st/api-client`. A
browser-side query needs a reason — live updates, or state driven by user
interaction.

**Switching tenant clears the entire query cache.** Not the affected keys, all of
them. A user who belongs to two tenants will otherwise be shown the previous
tenant's cached response under the new tenant's heading, and no test written
against a single tenant will ever catch it.

Never send a tenant identifier with a request. The tenant comes from the token.

## Environment variables

`NEXT_PUBLIC_` means "ship this to every browser that loads the app." Nothing
tenant-related and no secret carries that prefix.

Environment variables are parsed through a zod schema in `src/lib/env.ts` at
startup, so a malformed value fails the build rather than a request at an
inconvenient hour.

Deployment checks key off `APP_ENV` (`local` | `staging` | `production`), never
`NODE_ENV`. `next build` sets `NODE_ENV=production` on a developer's laptop too,
so a rule written against it fires during ordinary local builds and teaches
everyone to ignore it. `APP_ENV` is set by the deployment and nowhere else.

## Metadata

This app is not indexed, so metadata serves exactly one purpose here: the browser
tab. The root layout sets `title.template`; every route exports its own `title`.
Without that, a user with ten tabs open sees the same word ten times.

Nothing else — no description, OG image, or canonical tag. See Indexing.

## Language

The locale is `en-IE`, not `en`. No user-facing literal belongs in a component —
all strings come from a catalog. Dates, numbers, and currency go
through the `Intl` helpers in `@st/shared`; a raw `toLocaleString()` or a
hand-built date format is a bug.

There is no locale segment in this app's URLs. Language comes from the user
record. Adding a second locale must not require touching routing.

## Indexing

This app must never be indexed. `robots.ts` serves `Disallow: /`. Do not add a
sitemap, canonical tags, or marketing metadata here — that is `apps/marketing`'s
job.
