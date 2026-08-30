import { z } from "zod";

/**
 * Environment, validated once at startup.
 *
 * The point is to fail while someone is looking — a build or a boot — rather
 * than at 3am inside a request handler that assumed a variable was set.
 *
 * On the Clerk keys being optional: `clerk init` provisioned a temporary
 * keyless application, which supplies credentials through .clerk/keyless.json
 * rather than the environment, so requiring them here would break local work
 * and the production build alike. What is enforced instead is the mistake that
 * actually costs something — a malformed key, or test credentials reaching
 * production. Make them required once the application is claimed and real keys
 * exist.
 */
const schema = z
  .object({
    /**
     * Where this instance is running — not how it was compiled.
     *
     * NODE_ENV cannot answer this: `next build` sets it to "production" on a
     * developer's laptop too, so keying a deployment check off it fires during
     * ordinary local builds. Set APP_ENV in the deployment environment and
     * nowhere else.
     */
    APP_ENV: z.enum(["local", "staging", "production"]).default("local"),

    /** Server-only. Never prefixed NEXT_PUBLIC_ — that would ship it to every
     *  browser that loads the app. */
    CLERK_SECRET_KEY: z.string().startsWith("sk_").optional(),

    /** Safe to expose: it identifies the Clerk instance, it does not authorize
     *  anything. */
    NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY: z.string().startsWith("pk_").optional(),
  })
  .superRefine((env, ctx) => {
    if (env.APP_ENV !== "production") return;

    for (const [key, value] of [
      ["CLERK_SECRET_KEY", env.CLERK_SECRET_KEY],
      [
        "NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY",
        env.NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY,
      ],
    ] as const) {
      if (value?.includes("_test_")) {
        ctx.addIssue({
          code: "custom",
          path: [key],
          message: `${key} is a test key, but APP_ENV is production.`,
        });
      }
    }
  });

// Properties are read individually rather than from `process.env` as a whole:
// Next.js inlines NEXT_PUBLIC_* by literal reference, and a spread would leave
// it undefined in the browser bundle.
const parsed = schema.safeParse({
  APP_ENV: process.env.APP_ENV,
  CLERK_SECRET_KEY: process.env.CLERK_SECRET_KEY,
  NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY:
    process.env.NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY,
});

if (!parsed.success) {
  throw new Error(`Invalid environment:\n${z.prettifyError(parsed.error)}`);
}

export const env = parsed.data;
