/**
 * What every app shares: types, schemas, and the helpers that turn data into
 * text. No runtime that assumes a DOM — apps/mobile imports this too.
 */
export {
  DEFAULT_LOCALE,
  isLocale,
  LOCALES,
  type Locale,
  languageOf,
} from "./locale.ts";
