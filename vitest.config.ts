import { defineConfig } from "vitest/config";

/**
 * One config at the root, like biome.json and for the same reason: a second
 * one is how two packages quietly stop agreeing on what a passing test means.
 *
 * Only `packages/**` is collected. Business logic lives in apps/api and is
 * tested there; what the frontend owns that is worth unit testing is the pure
 * data handling in the shared packages — catalog resolution and Intl
 * formatting. A component test suite is not what this is for.
 */
export default defineConfig({
  test: {
    include: ["packages/*/src/**/*.test.ts"],
  },
});
