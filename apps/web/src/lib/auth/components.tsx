import { ClerkProvider, Show } from "@clerk/nextjs";
import { shadcn } from "@clerk/ui/themes";
import type { ReactNode } from "react";

/**
 * The provider's UI, renamed to ours.
 *
 * These are thin on purpose. The point is not to add behavior but to keep
 * `@clerk/*` out of the other several hundred files, so replacing the vendor is
 * a rewrite of this folder rather than a search across the app.
 *
 * That earned its keep immediately: Clerk Core 3 removed <SignedIn> and
 * <SignedOut> in favor of <Show when="...">. The exports still resolve and
 * still typecheck — they throw at runtime instead. Absorbing that here meant
 * the change stopped at this file.
 */
export {
  SignIn as SignInForm,
  SignInButton,
  SignOutButton,
  UserButton as UserMenu,
} from "@clerk/nextjs";

/**
 * There is no sign-up. Not an unlinked route, not a hidden component — the app
 * has no way to create an account at all.
 *
 * Access is by invitation, and invitations are ours: our own flow creates the
 * identity through Clerk's Backend API and the person then signs in. That is the
 * cost the root AGENTS.md accepts in exchange for keeping membership out of the
 * provider, and it is also what lets this route simply not exist.
 *
 * Removing the route also removes Clerk's "Don't have an account? Sign up"
 * footer — verified in the browser, it renders nothing where that link was.
 *
 * That is cosmetic, though. The provider's own hosted sign-up remains reachable
 * until public sign-up is switched off on the instance, which needs `clerk link`
 * first. The door is shut in our code and still ajar on Clerk's side.
 */

/**
 * Clerk's own UI, wearing our theme.
 *
 * `shadcn` reads the same CSS variables the rest of the app uses — the ones
 * @st/tokens generates — so the sign-in form follows our indigo and flips with
 * the rest of the page. It is not a second palette to keep in sync; changing a
 * color in packages/tokens/src/theme.ts moves this too.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  return (
    <ClerkProvider appearance={{ theme: shadcn }}>{children}</ClerkProvider>
  );
}

export function SignedIn({ children }: { children: ReactNode }) {
  return <Show when="signed-in">{children}</Show>;
}

export function SignedOut({ children }: { children: ReactNode }) {
  return <Show when="signed-out">{children}</Show>;
}

/**
 * Note what is deliberately not exported.
 *
 * `Show` also accepts `when={{ role }}`, `when={{ permission }}` and
 * `when={{ plan }}`. Those read authorization out of the provider, which is
 * exactly the coupling this repo refuses: roles and tenant membership are rows
 * in our own catalog, keyed by the provider's user id. Gate on data from our
 * API instead — and remember that hiding UI is not authorization either way.
 *
 * Likewise absent: OrganizationSwitcher, CreateOrganization, OrganizationList,
 * useOrganization. Clerk Organizations is not enabled and must not be. See the
 * root AGENTS.md for why the invite screens we build ourselves are the cheaper
 * side of that trade.
 */
