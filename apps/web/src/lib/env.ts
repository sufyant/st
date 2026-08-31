import { z } from "zod";

/**
 * Environment, validated once at startup.
 *
 * The point is to fail while someone is looking — a build or a boot — rather
 * than at 3am inside a request handler that assumed a variable was set.
 *
 * Every variable this app reads is declared here. One that is set in a shell
 * and nowhere else exists on exactly one machine, which is the failure this
 * file was written to prevent.
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
    CLERK_SECRET_KEY: z.string().startsWith("sk_"),

    /** Safe to expose: it identifies the Clerk instance, it does not authorize
     *  anything. */
    NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY: z.string().startsWith("pk_"),

    /**
     * Where Clerk sends an unauthenticated visitor, and where it returns them
     * when nothing else asked for a destination.
     *
     * There is deliberately no sign-up counterpart. Access is by invitation,
     * the route does not exist, and the instance runs
     * `sign_up_mode=restricted`; a NEXT_PUBLIC_CLERK_SIGN_UP_URL would re-add
     * "Sign up" links to Clerk's own components pointing at a 404.
     */
    NEXT_PUBLIC_CLERK_SIGN_IN_URL: z
      .string()
      .startsWith("/")
      .default("/sign-in"),
    NEXT_PUBLIC_CLERK_SIGN_IN_FALLBACK_REDIRECT_URL: z
      .string()
      .startsWith("/")
      .default("/"),
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
      if (value.includes("_test_")) {
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
  NEXT_PUBLIC_CLERK_SIGN_IN_URL: process.env.NEXT_PUBLIC_CLERK_SIGN_IN_URL,
  NEXT_PUBLIC_CLERK_SIGN_IN_FALLBACK_REDIRECT_URL:
    process.env.NEXT_PUBLIC_CLERK_SIGN_IN_FALLBACK_REDIRECT_URL,
});

if (!parsed.success) {
  throw new Error(`Invalid environment:\n${z.prettifyError(parsed.error)}`);
}

export const env = parsed.data;
