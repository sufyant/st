import { defineConfig } from "vitest/config";

/**
 * One config at the root, like biome.json and for the same reason: a second
 * one is how two packages quietly stop agreeing on what a passing test means.
 *
 * Collected from `apps/*` and `packages/*` alike, but the trigger is the same
 * either way: pure data handling worth pinning down — locale resolution, Intl
 * formatting. Business logic lives in apps/api and is tested there, and a
 * component test suite is not what this is for.
 */
export default defineConfig({
  test: {
    include: ["apps/*/src/**/*.test.ts", "packages/*/src/**/*.test.ts"],
  },
});
