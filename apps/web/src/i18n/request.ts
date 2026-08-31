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
 * Catalogs are keyed by language, so the import takes the subtag: one `en`
 * serves `en-IE` and `en-GB`.
 *
 * Two of them, merged. `@st/i18n` holds the strings that repeat across apps;
 * `messages/` here holds the ones only this app shows. A single shared catalog
 * would make a copy fix in web force a redeploy of admin and marketing.
 */
export default getRequestConfig(async () => {
  const locale = await resolveLocale();
  const language = languageOf(locale);

  return {
    locale,
    messages: {
      ...(await import(`@st/i18n/messages/${language}.json`)).default,
      ...(await import(`../../messages/${language}.json`)).default,
    },
  };
});
