import { Button } from "@st/ui/components/button";
import Link from "next/link";
import { getTranslations } from "next-intl/server";

/**
 * Renders inside the root layout, so it has the provider, the theme and the
 * fonts. Only `global-error` loses those.
 */
export default async function NotFound() {
  const t = await getTranslations("common");

  return (
    <main className="mx-auto flex min-h-screen max-w-md flex-col items-center justify-center gap-4 p-10 text-center">
      <h1 className="font-heading text-2xl font-semibold">
        {t("notFound.title")}
      </h1>
      <p className="text-muted-foreground text-sm">{t("notFound.body")}</p>
      {/*
        Base UI composes through `render`, not `asChild`. `nativeButton={false}`
        goes with it: this renders an <a>, and leaving the default on makes the
        component apply native button semantics to an element that has none.
      */}
      <Button nativeButton={false} render={<Link href="/" />}>
        {t("action.goHome")}
      </Button>
    </main>
  );
}
