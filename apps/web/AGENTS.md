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

Of those packages only `@st/ui` exists today. `@st/tokens`, `@st/i18n`,
`@st/shared` and `@st/api-client` are where that code will live — do not invent a
local substitute for one, and do not import it before it is built. Colors
currently live in `@st/ui`'s stylesheet and move to `@st/tokens` when mobile
starts.

A component in `src/components` that turns out to be generic does not move to
`@st/ui` until a second app needs it. Duplication is cheaper than a premature
shared abstraction that every app must then be redeployed for.

## Auth

`@clerk/nextjs` may be imported **only** from `src/lib/auth/**`. The rest of the
app imports from `@/lib/auth`, which exports our own session shape — not Clerk's
types. A lint rule enforces this; if it fires, the fix is to widen the wrapper,
never to add an exception.

This app authenticates against the **customer** Clerk instance. `apps/admin` uses
a different one. Never share an instance, a key, or a session between them.

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

Environment variables are parsed through a zod schema at startup, so a missing or
malformed value fails the build. The alternative is finding out from a production
error at an inconvenient hour.

## Metadata

This app is not indexed, so metadata serves exactly one purpose here: the browser
tab. The root layout sets `title.template`; every route exports its own `title`.
Without that, a user with ten tabs open sees the same word ten times.

Nothing else — no description, OG image, or canonical tag. See Indexing.

## Language

English only today, but nothing may assume it. No user-facing literal belongs in
a component — all strings come from a catalog. Dates, numbers, and currency go
through the `Intl` helpers in `@st/shared`; a raw `toLocaleString()` or a
hand-built date format is a bug.

There is no locale segment in this app's URLs. Language comes from the user
record. Adding a second locale must not require touching routing.

## Indexing

This app must never be indexed. `robots.ts` serves `Disallow: /`. Do not add a
sitemap, canonical tags, or marketing metadata here — that is `apps/marketing`'s
job.
