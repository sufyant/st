# @st/web

The customer-facing dashboard. Everything here sits behind authentication and is
scoped to one tenant.

## Running it

From the repo root, so pnpm resolves the workspace:

```
pnpm --filter @st/web dev
```

You need `apps/web/.env.local` first — copy `.env.example` and fill it in, or
run `clerk env pull` from this directory to have the Clerk keys written for you.
The app will not start without them: `src/lib/env.ts` validates the environment
before anything else runs.

Other commands:

```
pnpm --filter @st/web build      # production build (standalone output)
pnpm --filter @st/web typecheck
pnpm lint                        # repo-wide, from the root
pnpm format                      # repo-wide, from the root
```

## Everything else

`AGENTS.md` in this directory is the real documentation: stack, where code goes,
the auth boundary, rendering rules, i18n, and deployment. Read the repo root's
`AGENTS.md` first.
