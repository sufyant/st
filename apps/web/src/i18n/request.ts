import {
  type Catalog,
  type CatalogLoader,
  FALLBACK_LANGUAGE,
  loadSharedMessages,
  resolveCatalog,
} from "@st/i18n";
import { getRequestConfig } from "next-intl/server";
import { resolveLocale } from "@/lib/locale";

/**
 * This app's own catalogs, resolved exactly as @st/i18n's are: a folder per
 * BCP-47 tag, and a regional folder is merged over its language rather than
 * replacing it.
 *
 * Written as a literal map rather than a template-literal import so the set of
 * catalogs is a value the compiler and the bundler can both see. A dynamic
 * path would make every folder a possible import and defer a missing one to a
 * runtime 500.
 */
const catalogs = {
  en: () => import("../../messages/en/app.json"),
} satisfies Record<string, CatalogLoader<Catalog>>;

/**
 * next-intl's per-request configuration.
 *
 * The locale is returned explicitly because nothing routes on it: there is no
 * locale segment in this app's URLs and no middleware deriving one, so without
 * this next-intl has nothing to infer from.
 *
 * Messages are merged from two places by design. `@st/i18n` holds the strings
 * that genuinely repeat across apps; `messages/` here holds the ones only this
 * app shows. A single shared catalog would make a copy fix in web force a
 * redeploy of admin and marketing.
 */
export default getRequestConfig(async () => {
  const locale = await resolveLocale();

  return {
    // The full tag, not the language. Intl needs the region: `en` alone
    // formats dates and numbers the American way.
    locale,
    messages: {
      ...(await loadSharedMessages(locale)),
      app: await resolveCatalog(catalogs, locale, FALLBACK_LANGUAGE, "app"),
    },
  };
});
