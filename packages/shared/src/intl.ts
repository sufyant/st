import type { Locale } from "./locale.ts";

/**
 * The only place dates, numbers and currency get turned into text.
 *
 * A `toLocaleString()` call in a component is a bug — not because it is wrong
 * on the machine that wrote it, but because it silently picks up whatever
 * locale that machine has, and nobody sees the difference until a customer in
 * another country reads the wrong month.
 *
 * `Intl` constructors are expensive and get called per row of a table, so each
 * one is built once per (locale, options) pair and kept.
 */
const cache = new Map<string, Intl.DateTimeFormat | Intl.NumberFormat>();

function cached<T extends Intl.DateTimeFormat | Intl.NumberFormat>(
  kind: string,
  locale: Locale,
  options: object,
  build: () => T,
): T {
  const key = `${kind}:${locale}:${JSON.stringify(options)}`;
  let formatter = cache.get(key);
  if (!formatter) {
    formatter = build();
    cache.set(key, formatter);
  }
  return formatter as T;
}

export function formatDate(
  value: Date | number,
  locale: Locale,
  options: Intl.DateTimeFormatOptions = { dateStyle: "medium" },
): string {
  return cached(
    "date",
    locale,
    options,
    () => new Intl.DateTimeFormat(locale, options),
  ).format(value);
}

export function formatNumber(
  value: number,
  locale: Locale,
  options: Intl.NumberFormatOptions = {},
): string {
  return cached(
    "number",
    locale,
    options,
    () => new Intl.NumberFormat(locale, options),
  ).format(value);
}

/**
 * An amount of money, as it is stored: minor units plus the currency it was
 * recorded in.
 *
 * Both halves are required because formatting can guess neither. The currency
 * is a property of the transaction — an amount recorded in euro is euro no
 * matter who opens the page — while the locale decides only how it is written.
 */
export type Money = {
  /** Cents, pence, yen. Never a float: 0.1 + 0.2 is not 0.3. */
  minorUnits: number;
  /** ISO 4217, e.g. "EUR". */
  currency: string;
};

/**
 * How many minor units make a major one is a property of the currency: 100 for
 * EUR, 1 for JPY, 1000 for KWD. `Intl` already knows all of them, so the
 * exponent is read from it rather than kept in a table here that would be
 * wrong for some currency nobody tested.
 */
function minorUnitDigits(locale: Locale, currency: string): number {
  return (
    new Intl.NumberFormat(locale, {
      style: "currency",
      currency,
    }).resolvedOptions().maximumFractionDigits ?? 2
  );
}

export function formatMoney(
  { minorUnits, currency }: Money,
  locale: Locale,
  options: Intl.NumberFormatOptions = {},
): string {
  const digits = minorUnitDigits(locale, currency);
  return formatNumber(minorUnits / 10 ** digits, locale, {
    style: "currency",
    currency,
    ...options,
  });
}
