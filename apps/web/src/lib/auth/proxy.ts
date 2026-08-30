import { clerkMiddleware } from "@clerk/nextjs/server";

/**
 * Edge auth. Re-exported by src/proxy.ts, which Next.js loads by convention —
 * that indirection exists so the SDK import stays inside this folder.
 *
 * Next.js 16 renamed middleware.ts to proxy.ts.
 *
 * This establishes who the request is from. It does not decide what they may
 * reach: tenancy and permissions are resolved by the API, per request.
 */
export const authProxy = clerkMiddleware();
