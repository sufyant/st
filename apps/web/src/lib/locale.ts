import { DEFAULT_LOCALE, type Locale } from "@st/shared";

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
