/**
 * Locales, as full tags.
 *
 * Never a bare language. `en` alone leaves `Intl` guessing, and it guesses
 * American: `03/09` comes out as March 9th for a reader who wrote September
 * 3rd. That is an operational error, not a cosmetic one.
 */
export const LOCALES = ["en-IE"] as const;

export type Locale = (typeof LOCALES)[number];

export const DEFAULT_LOCALE: Locale = "en-IE";

export function isLocale(value: string): value is Locale {
  return (LOCALES as readonly string[]).includes(value);
}

/**
 * The language subtag a catalog is keyed by — `en-IE` and `en-GB` both read
 * the one `en` catalog.
 *
 * Maintaining two English catalogs that differ in a handful of words is a cost
 * with no return. Formatting still gets the whole tag; only the messages fall
 * back.
 *
 * Parsed by `Intl.Locale` rather than by splitting on a hyphen, because a tag
 * is not simply "the part before the dash" — `zh-Hant-TW` and the grandfathered
 * tags are exactly the cases a hand-rolled split gets wrong.
 */
export function languageOf(locale: string): string {
  return new Intl.Locale(locale).language;
}
