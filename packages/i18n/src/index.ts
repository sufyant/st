import { DEFAULT_LOCALE, languageOf } from "@st/shared";
import commonEn from "../messages/en/common.json";
import { type Catalog, type CatalogLoader, resolveCatalog } from "./catalog.ts";

/**
 * Message catalogs for the strings that genuinely repeat across apps.
 *
 * Everything else lives in the app that displays it. A single shared catalog
 * would mean a copy fix in `web` forces a redeploy of `admin` and `marketing`
 * — which is the same reason product components stay in their app.
 *
 * Folders are keyed by BCP-47 tag, and the tag is as short as it needs to be.
 * `en` is the whole English catalog; an `en-GB` folder would hold only the
 * words Britain spells differently and would be merged on top. Formatting is a
 * separate axis and always takes the full tag — see `@st/shared`.
 *
 * There is deliberately no `errors` namespace yet. It is reserved for the
 * `Entity.ErrorName` codes the API returns, and the API does not exist. It is
 * added with the first real code, not in anticipation of one.
 */

/**
 * Loaded on demand rather than imported at the top. With one language the
 * difference is nothing; with five, a static import puts every catalog in the
 * bundle so that one can be read.
 */
const catalogs = {
  en: async () => ({
    default: {
      common: (await import("../messages/en/common.json")).default,
      auth: (await import("../messages/en/auth.json")).default,
    },
  }),
} satisfies Record<string, CatalogLoader<Catalog>>;

export type SharedMessages = Awaited<
  ReturnType<(typeof catalogs)["en"]>
>["default"];

/** The language every app falls back to when a locale has no catalog. */
export const FALLBACK_LANGUAGE = languageOf(DEFAULT_LOCALE);

/** The shared namespaces for a locale, region overrides merged in. */
export function loadSharedMessages(locale: string): Promise<SharedMessages> {
  return resolveCatalog(catalogs, locale, FALLBACK_LANGUAGE, "@st/i18n");
}

export {
  type Catalog,
  type CatalogLoader,
  catalogChain,
  deepMerge,
  resolveCatalog,
} from "./catalog.ts";

/**
 * English `common`, imported statically.
 *
 * For the one case the loader above cannot serve: a global error boundary
 * replaces the root layout, so there is no provider, no request config, and
 * possibly no working locale resolution — that may be exactly what broke.
 * Falling back to English there is not a missing feature; it is the only
 * answer that cannot itself fail.
 */
export const fallbackCommon = commonEn;
