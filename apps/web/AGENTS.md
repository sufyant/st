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

There are no tests in this app. Vitest collects `packages/**` only — what this
app owns is components and routes, and a component suite is not the trigger the
root AGENTS.md describes. The pure logic it depends on is tested where it lives,
in `@st/shared`. `pnpm test` from the root runs it.

`typecheck` runs `next typegen` first. `LayoutProps`, `PageProps` and the route
literals are generated into `.next/types`, so on a fresh clone — a colleague's,
or CI's — `tsc` alone fails on types that exist only after a build has run once.

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

`@st/ui`, `@st/tokens`, `@st/shared` and `@st/i18n` exist today.
`@st/api-client` is where that code will live — do not invent a local
substitute for it, and do not import it before it is built.

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

Verified on the customer instance: `auth_access_control.sign_up_mode` is
`restricted`, so an account can only be created against an invitation. Clerk's
default is `public` — this is a setting someone changed, and one a future
`config put` could silently change back. Re-check it with `clerk config pull`
alongside `organization_settings.enabled`, which must stay `false`.

Only a development instance exists today (`clerk doctor` reports production as
not configured). **Neither setting carries over to production** — a new instance
starts at Clerk's defaults, which means public sign-up and this door open again.
Re-verify both on the production instance before the first real deployment.

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

`error.tsx` and `global-error.tsx` carry `"use client"` because the framework
requires it — an error boundary can only catch a render in the tree below it
from the client. They are the exception this rule allows for, not a precedent.

`global-error.tsx` replaces the root layout, so it has no provider, no fonts, no
theme class and no stylesheet. It imports `@st/i18n`'s English JSON directly
and its styling is inline: it is the page for when the machinery is broken, and
it must not depend on the machinery.

There is deliberately no root `loading.tsx`. A suspense boundary with nothing to
suspend on is a spinner that flashes for no reason. It arrives with the first
screen that actually waits on data.

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
inconvenient hour. Every variable this app reads is declared there, Clerk's
included — one that is set in a shell and nowhere else exists on exactly one
machine, which is the failure the schema was written to prevent.

The Clerk keys are required. The application is claimed and linked, so real
keys exist and a build without them should fail. Do not reintroduce a keyless
fallback: it supplies credentials outside the schema, which is the one thing
the schema is for.

There is deliberately no `NEXT_PUBLIC_CLERK_SIGN_UP_URL`. Setting one re-adds
"Sign up" links to Clerk's own components, pointing at a route that does not
exist — see Access is by invitation.

Deployment checks key off `APP_ENV` (`local` | `staging` | `production`), never
`NODE_ENV`. `next build` sets `NODE_ENV=production` on a developer's laptop too,
so a rule written against it fires during ordinary local builds and teaches
everyone to ignore it. `APP_ENV` is set by the deployment and nowhere else.

## Metadata

This app is not indexed, so metadata serves exactly one purpose here: the browser
tab. The root layout sets `title.template`; every route exports its own `title`.
Without that, a user with ten tabs open sees the same word ten times.

Nothing else — no description, OG image, or canonical tag. See Indexing.

`app/icon.tsx` generates the tab icon at build time from `@st/tokens` rather
than committing a `.png`. A static file would put a hex code outside
`theme.ts`, and it is the copy nobody remembers to update when the brand
changes.

`layout.tsx` exports a `viewport` with `colorScheme` and a `themeColor` per
scheme, both read from `@st/tokens`. These are not metadata in the SEO sense:
`colorScheme` is what makes the browser's own widgets — native selects,
scrollbars — render dark, and `themeColor` is the mobile address bar.

## Language

The locale is `en-IE`, not `en`. No user-facing literal belongs in a component —
all strings come from a catalog. Dates, numbers, and currency go
through the `Intl` helpers in `@st/shared`; a raw `toLocaleString()` or a
hand-built date format is a bug.

There is no locale segment in this app's URLs. Language comes from the user
record. Adding a second locale must not require touching routing.

Translation runs on `next-intl`, configured without i18n routing:

| Piece | Where |
|---|---|
| Which locale this request is | `src/lib/locale.ts` |
| Handing the catalogs to next-intl | `src/i18n/request.ts` |
| Strings shared across apps | `@st/i18n` (`common`, `auth`) |
| Strings only this app shows | `messages/<language>.json` |
| Date, number and money formatting | `@st/shared` — added with the first value that needs it |

`resolveLocale()` in `src/lib/locale.ts` is the single place the locale is
decided, and today it returns the one locale we support. When `@st/api-client`
exists it reads the user record instead — that one function is the whole change,
which is what keeps "adding a language is not a refactor" true.

One JSON file per language, not per locale: `messages/en.json` serves `en-IE`.
The words are the same; only the formatting differs, and formatting does not
come from the catalog. `request.ts` imports the app's file and `@st/i18n`'s by
the language subtag and spreads them together — next-intl's documented shape
for messages split across packages.

That is the other axis, and it always takes the full tag: `en` and `en-US` write
3 September as `9/3/26`, `en-IE` writes `03/09/2026`. Same words, and a date read
as the wrong month.

Clerk's forms are translated by Clerk, not by our catalogs, and `localization`
is merged over Clerk's `enUS` default — so `AuthProvider` passes only what is
actually wrong for us, which is dates.

That is dates. Clerk pins a locale inside its own templates
(`{{ date | numeric('en-US') }}`), so its timestamps render American no matter
what the rest of the page does. `AuthProvider` rebuilds that block with our tag
interpolated, which is correct for every locale rather than for a listed one.
Switching to Clerk's `enGB` instead would be the obvious move and the wrong one:
of its 57 differences from `enUS`, 47 are Organizations and SSO wording we never
render, one is a regression, it costs 69KB on every page, and it pins `en-GB`
when we are `en-IE`.

Wording needs no override: Clerk's default is English and so is ours. A second
language passes its `@clerk/localizations` resource alongside the date block.

## Indexing

This app must never be indexed. `robots.ts` serves `Disallow: /`, and every
response also carries `X-Robots-Tag: noindex, nofollow` from `next.config.ts` —
robots.txt is a request a crawler may not have made, the header is on the
response it definitely received. Do not add a sitemap, canonical tags, or
marketing metadata here; that is `apps/marketing`'s job.

## Deployment

`next.config.ts` sets `output: "standalone"`, so the build emits
`.next/standalone` with a `server.js` and only the traced subset of
`node_modules`. `outputFileTracingRoot` points at the repo root — without it,
tracing stops at the symlinks to `@st/ui` and `@st/tokens` and the container
starts, then dies on a missing module. The workspace layout survives into the
output, so the entrypoint is `apps/web/server.js`, not `server.js`.

`Dockerfile` builds from the **repo root** as context, not from this directory.
`NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` is a build argument because Next inlines
`NEXT_PUBLIC_*` into the client bundle — which means an image belongs to one
Clerk instance, and staging and production are two images rather than one image
with two configurations. The secret key is never built in; it arrives at
runtime, where `src/lib/env.ts` validates it again as the server starts.

Security headers are also in `next.config.ts` rather than in the proxy, so they
cover static assets and every path the proxy's matcher skips. The one CSP
directive set is `frame-ancestors`; a `script-src` policy needs per-request
nonces threaded through the layout, and a half-written one that everybody
disables is worse than none. `Cross-Origin-Opener-Policy` is
`same-origin-allow-popups` on purpose: `same-origin` would break Clerk's Google
sign-in, which talks back through `window.opener`.

This app's CI jobs are `web-build` and `web-image` in
`.github/workflows/ci.yml`, gated on the `web` path filter. They use
placeholder Clerk keys: they compile the app and build the image, they never
talk to Clerk. Why there is one workflow rather than one per app, and where a
new app's jobs go, is in the root AGENTS.md.
