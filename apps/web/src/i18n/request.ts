import { languageOf } from "@st/shared";
import { getRequestConfig } from "next-intl/server";
import { resolveLocale } from "@/lib/locale";

/**
 * next-intl's per-request configuration.
 *
 * The locale is returned explicitly because nothing routes on it: there is no
 * locale segment in this app's URLs and no middleware deriving one, so without
 * this next-intl has nothing to infer from. It is the full tag — `Intl` needs
 * the region, and `en` alone formats the American way.
 *
 * Catalogs are keyed by language, so the import takes the subtag: one `en.json`
 * serves `en-IE` and `en-GB`.
 *
 * This app owns every string it shows. There is no shared catalog — see the
 * Language section of the root AGENTS.md for what would have to be true before
 * one existed.
 */
export default getRequestConfig(async () => {
  const locale = await resolveLocale();

  return {
    locale,
    messages: (await import(`../../messages/${languageOf(locale)}.json`))
      .default,
  };
});
