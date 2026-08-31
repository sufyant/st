/**
 * What every app shares: types, schemas, and the formatters that turn data
 * into text. No runtime that assumes a DOM — apps/mobile imports this too.
 */
export {
  formatDate,
  formatMoney,
  formatNumber,
  type Money,
} from "./intl.ts";
export {
  DEFAULT_LOCALE,
  isLocale,
  LOCALES,
  type Locale,
  languageOf,
} from "./locale.ts";
