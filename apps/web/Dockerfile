# syntax=docker/dockerfile:1

# Built from the repo root, not from apps/web:
#
#   docker build -f apps/web/Dockerfile \
#     --build-arg NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY=pk_live_... \
#     -t st-web .
#
# The context has to be the workspace root because the build needs the
# lockfile and the packages/* this app imports.

ARG NODE_VERSION=24-alpine

# ── deps ──────────────────────────────────────────────────────────────────
# Manifests only, so a source-only change reuses this layer's install.
FROM node:${NODE_VERSION} AS deps
ENV COREPACK_ENABLE_DOWNLOAD_PROMPT=0
WORKDIR /repo
RUN corepack enable
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY apps/web/package.json apps/web/
COPY packages/ui/package.json packages/ui/
COPY packages/tokens/package.json packages/tokens/
RUN pnpm install --frozen-lockfile

# ── build ─────────────────────────────────────────────────────────────────
FROM node:${NODE_VERSION} AS build
ENV COREPACK_ENABLE_DOWNLOAD_PROMPT=0
WORKDIR /repo
RUN corepack enable
# Installed tree first, then the source over it. .dockerignore excludes
# node_modules from the context, so the second COPY cannot clobber the first.
COPY --from=deps /repo ./
COPY . .

# Next inlines every NEXT_PUBLIC_* value into the client bundle at build time,
# so the publishable key is baked into the image and the image is therefore
# specific to one Clerk instance. Staging and production are two images, not
# one image with two sets of variables.
ARG NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY
ENV NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY=${NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY}

# The secret is NOT baked in. src/lib/env.ts validates the environment at
# module evaluation, which the build triggers, so a syntactically valid
# placeholder gets past it. The real secret arrives at runtime — and env.ts
# runs again when the container's server starts, so the check that matters
# still happens, against the real value.
ENV CLERK_SECRET_KEY=sk_test_placeholder_build_only

RUN pnpm --filter @st/web build

# ── runner ────────────────────────────────────────────────────────────────
FROM node:${NODE_VERSION} AS runner
WORKDIR /app
ENV NODE_ENV=production
ENV PORT=3000 HOSTNAME=0.0.0.0

# `standalone` already contains the traced subset of node_modules and a
# server.js, so there is no install here and no package manager in the final
# image. Static assets are not copied into it by the build — normally a CDN's
# job, but Container Apps serves them from the container, so they come along.
COPY --from=build --chown=node:node /repo/apps/web/.next/standalone ./
COPY --from=build --chown=node:node /repo/apps/web/.next/static ./apps/web/.next/static

# node:alpine ships a non-root `node` user. Creating another one buys nothing.
USER node
EXPOSE 3000

# outputFileTracingRoot is the repo root, so standalone preserves the
# workspace layout and the entrypoint keeps its path.
CMD ["node", "apps/web/server.js"]
