"use client";

import { Button } from "@st/ui/components/button";
import { useTranslations } from "next-intl";

/**
 * `"use client"` here is the framework's requirement, not a choice: an error
 * boundary has to catch errors thrown while rendering the tree below it, which
 * only a client component can do. This is the exception the Rendering section
 * of AGENTS.md leaves room for, not a precedent for pages.
 */
// Named for what it is rather than for the file: `Error` here would shadow the
// global, which is both a lint error and a genuinely confusing thing to read.
export default function ErrorBoundary({
  reset,
}: {
  error: Error;
  reset: () => void;
}) {
  const t = useTranslations("common");

  return (
    <main className="mx-auto flex min-h-screen max-w-md flex-col items-center justify-center gap-4 p-10 text-center">
      <h1 className="font-heading text-2xl font-semibold">
        {t("unexpected.title")}
      </h1>
      <p className="text-muted-foreground text-sm">{t("unexpected.body")}</p>
      {/*
        The error object is deliberately not rendered. A message thrown on the
        server can carry a query, a path, or an id, and this page is the one
        place a user would be shown it.
      */}
      <Button onClick={reset}>{t("action.retry")}</Button>
    </main>
  );
}
