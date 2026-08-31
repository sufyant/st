import { Button } from "@st/ui/components/button";
import { getTranslations } from "next-intl/server";
import { ThemeToggle } from "@/components/theme-toggle";
import {
  SignedIn,
  SignedOut,
  SignInButton,
  UserMenu,
} from "@/lib/auth/components";
import { getSession } from "@/lib/auth/server";

// Temporary scaffolding: proves the token pipeline and both themes render.
// Delete once the first real screen lands.

// Token and variant names, not copy. They are identifiers that happen to be
// rendered, and translating them would make the swatch lie about what it shows.
const semantic = [
  { name: "primary", bg: "bg-primary", fg: "text-primary-foreground" },
  { name: "success", bg: "bg-success", fg: "text-success-foreground" },
  { name: "warning", bg: "bg-warning", fg: "text-warning-foreground" },
  {
    name: "destructive",
    bg: "bg-destructive",
    fg: "text-destructive-foreground",
  },
];

export default async function Page() {
  const [session, t, auth] = await Promise.all([
    getSession(),
    getTranslations("app.palette"),
    getTranslations("auth"),
  ]);

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-10 p-10">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="font-heading text-2xl font-semibold">{t("title")}</h1>
          <p className="text-muted-foreground text-sm">{t("subtitle")}</p>
        </div>
        <div className="flex items-center gap-2">
          <SignedOut>
            <SignInButton>
              <Button>{auth("signIn")}</Button>
            </SignInButton>
          </SignedOut>
          <SignedIn>
            <UserMenu />
          </SignedIn>
          <ThemeToggle />
        </div>
      </header>

      <section className="text-muted-foreground rounded-lg border p-4 text-sm">
        {session
          ? auth("signedInAs", {
              email: session.email ?? session.externalAuthId,
            })
          : `${auth("notSignedIn")} ${auth("byInvitation")} ${t("tenantFromApi")}`}
      </section>

      <section className="flex flex-wrap gap-2">
        <Button>Primary</Button>
        <Button variant="secondary">Secondary</Button>
        <Button variant="outline">Outline</Button>
        <Button variant="ghost">Ghost</Button>
        <Button variant="destructive">Destructive</Button>
        <Button variant="link">Link</Button>
      </section>

      <section className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {semantic.map((token) => (
          <div
            key={token.name}
            className={`${token.bg} ${token.fg} rounded-lg p-4 text-sm font-medium`}
          >
            {token.name}
          </div>
        ))}
      </section>
    </main>
  );
}
