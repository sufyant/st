<!-- BEGIN:nextjs-agent-rules -->

# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` (resolved from this file's directory; in monorepos the `next` package may not be visible from the repo root) before writing any code. Heed deprecation notices.

This block is written and re-added by `next dev` — verify at `node_modules/next/dist/server/lib/generate-agent-files.js`. Removing it from a diff only re-creates the uncommitted change; committing it with your work keeps the tree clean.

<!-- END:nextjs-agent-rules -->

# Project conventions

- New page/route with real logic (validation, protected behavior, data transforms) → add a test (Vitest for units, Playwright for flows). Pure UI shells don't need one.
- New page under `(app)` → add its label to `labels` in [dynamic-breadcrumb.tsx](src/components/dynamic-breadcrumb.tsx).
