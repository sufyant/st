import { auth, currentUser } from "@clerk/nextjs/server";
import type { Session } from "./types";

/**
 * Server-side session access. One of only three files permitted to import
 * `@clerk/*` — see the `noRestrictedImports` rule in the root biome.json.
 *
 * Everything Clerk returns is narrowed to our own `Session` here. Nothing
 * downstream should be able to tell which provider produced it.
 */

/** The signed-in person, or null. Never throws — callers decide what an
 *  anonymous visitor means for them. */
export async function getSession(): Promise<Session | null> {
  const { userId } = await auth();
  if (!userId) return null;

  const user = await currentUser();
  return {
    externalAuthId: userId,
    email: user?.primaryEmailAddress?.emailAddress ?? null,
  };
}

/**
 * The signed-in person, or a redirect to sign-in.
 *
 * This is a convenience for rendering, not a security boundary: the API
 * re-checks every request on its own. A page that forgets to call this leaks a
 * layout, not data.
 */
export async function requireSession(): Promise<Session> {
  const { userId, redirectToSignIn } = await auth();
  if (!userId) redirectToSignIn();

  const session = await getSession();
  if (!session) redirectToSignIn();
  return session as Session;
}
