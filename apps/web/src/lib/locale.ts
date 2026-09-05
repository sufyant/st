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

/**
 * The locale for this request.
 *
 * Language is a property of the person, not the device or the URL: an email
 * rendered by the API has to match what the app shows them, so it lives on the
 * user record. There is no locale segment in this app's URLs and there will
 * not be one — only apps/marketing needs that, and only because SEO does.
 *
 * TODO: read `locale` from the user record once @st/api-client exists. That is
 * the only line that changes; everything downstream already takes the locale
 * as an argument, which is the point of resolving it in exactly one place.
 *
 * Until then this returns the one locale we support, which is honest — the
 * alternative would be reading Accept-Language and quietly disagreeing with
 * the language the API sends the same person's email in.
 */
export async function resolveLocale(): Promise<Locale> {
  return DEFAULT_LOCALE;
}
