"use client";

import { useUser } from "@clerk/nextjs";
import type { Session } from "./types";

/**
 * Client-side session, narrowed to our shape.
 *
 * Prefer reading the session on the server and passing down only the fields a
 * component renders — see the Rendering section of this app's AGENTS.md. This
 * hook is for components that genuinely react to signing in or out.
 */
export function useSession(): {
  session: Session | null;
  isLoaded: boolean;
} {
  const { user, isLoaded } = useUser();

  return {
    isLoaded,
    session: user
      ? {
          externalAuthId: user.id,
          email: user.primaryEmailAddress?.emailAddress ?? null,
        }
      : null,
  };
}
