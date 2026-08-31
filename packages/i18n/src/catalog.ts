import { languageOf } from "@st/shared";

export type Catalog = Record<string, unknown>;
export type CatalogLoader<T extends Catalog> = () => Promise<{ default: T }>;

/**
 * Which catalogs to read for a locale, least specific first.
 *
 * `en-IE` reads `en`, then `en-IE` on top of it. This is RFC 4647 lookup: try
 * the most specific catalog, fall back through the less specific ones.
 *
 * The order matters because a region catalog is a *layer*, not a replacement.
 * Regional wording really does differ — "organise" against "organize" — but it
 * differs in a handful of strings out of hundreds. Treating `en-GB` as a whole
 * catalog would mean copying the other several hundred to change three, and
 * every later fix to the base would have to be applied twice or silently
 * wouldn't reach it.
 */
export function catalogChain(locale: string): string[] {
  const language = languageOf(locale);
  return language === locale ? [language] : [language, locale];
}

/**
 * Later objects win, key by key, all the way down.
 *
 * Deep rather than shallow because catalogs are nested by namespace: a shallow
 * merge of `{auth: {signIn, signOut}}` with `{auth: {signIn}}` would drop
 * `signOut`, which is exactly the override case this exists for.
 */
export function deepMerge<T extends Catalog>(layers: T[]): T {
  const out: Catalog = {};

  for (const layer of layers) {
    for (const [key, value] of Object.entries(layer)) {
      const existing = out[key];
      out[key] =
        isPlainObject(existing) && isPlainObject(value)
          ? deepMerge([existing, value])
          : value;
    }
  }

  return out as T;
}

function isPlainObject(value: unknown): value is Catalog {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * Load every catalog in the chain that exists and merge them.
 *
 * A locale with no catalog at all is a missing translation, not a broken page:
 * the fallback language is the correct thing to show. It is still worth
 * noticing, so it is reported rather than swallowed.
 */
export async function resolveCatalog<T extends Catalog>(
  catalogs: Partial<Record<string, CatalogLoader<T>>>,
  locale: string,
  fallbackLanguage: string,
  label: string,
): Promise<T> {
  const layers: T[] = [];

  for (const key of catalogChain(locale)) {
    const load = catalogs[key];
    if (load) layers.push((await load()).default);
  }

  if (layers.length === 0) {
    const fallback = catalogs[fallbackLanguage];
    if (!fallback) {
      throw new Error(
        `${label}: no catalog for "${locale}" and no "${fallbackLanguage}" to fall back to.`,
      );
    }
    console.warn(
      `${label}: no catalog for "${locale}"; using "${fallbackLanguage}".`,
    );
    return (await fallback()).default;
  }

  return deepMerge(layers);
}
