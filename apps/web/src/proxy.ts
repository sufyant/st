import { authProxy } from "@/lib/auth/proxy";

/**
 * Next.js loads this file by convention (16 renamed middleware.ts to proxy.ts).
 *
 * The handler is built in lib/auth so no provider SDK is imported outside that
 * folder, but Next.js parses this file statically and needs to see a real
 * function declaration and a literal `config` — neither survives a re-export.
 * Hence the delegation, typed off the wrapper so no vendor type leaks in here.
 */
export default function proxy(
  ...args: Parameters<typeof authProxy>
): ReturnType<typeof authProxy> {
  return authProxy(...args);
}

export const config = {
  matcher: [
    "/((?!_next|[^?]*\\.(?:html?|css|js(?!on)|jpe?g|webp|png|gif|svg|ttf|woff2?|ico|csv|docx?|xlsx?|zip|webmanifest)).*)",
    "/(api|trpc)(.*)",
  ],
};
