import { describe, expect, it } from "vitest";
import { formatDate, formatMoney, formatNumber } from "./intl.ts";

describe("formatMoney", () => {
  it("reads the minor-unit exponent from the currency, not from a constant", () => {
    // 2 digits, 0 digits and 3 digits. A hardcoded /100 gets two of these
    // wrong by two orders of magnitude, and only for the currencies nobody
    // wrote a test for.
    expect(formatMoney({ minorUnits: 123456, currency: "EUR" }, "en-IE")).toBe(
      "€1,234.56",
    );
    // "JP¥", not "¥": an Irish reader gets the country prefix so the symbol
    // cannot be mistaken for the local one. The symbol is the viewer's, the
    // currency is the data's.
    expect(formatMoney({ minorUnits: 123456, currency: "JPY" }, "en-IE")).toBe(
      "JP¥123,456",
    );
    // \u00A0, not a space. ICU separates the code from the amount with a
    // non-breaking space, which is invisible in a diff and will bite anyone
    // who ever compares a formatted amount as a string.
    expect(formatMoney({ minorUnits: 123456, currency: "KWD" }, "en-IE")).toBe(
      "KWD\u00A0123.456",
    );
  });

  it("keeps the currency and changes only how it is written", () => {
    // An amount recorded in euro stays euro no matter who opens the page.
    const euro = { minorUnits: 123456, currency: "EUR" } as const;
    expect(formatMoney(euro, "en-IE")).toContain("€");
    expect(formatNumber(0, "en-IE")).toBe("0");
  });
});

describe("formatDate", () => {
  it("uses the region, not the language", () => {
    // The failure this exists to prevent: en-IE reads 03/09 as 3 September,
    // en-US reads the same string as March 9th.
    const d = new Date(Date.UTC(2026, 8, 3));
    expect(
      formatDate(d, "en-IE", { dateStyle: "short", timeZone: "UTC" }),
    ).toBe("03/09/2026");
  });
});

describe("formatter cache", () => {
  it("does not hand back a formatter built for different options", () => {
    // The cache is keyed by locale plus options; a key that ignored options
    // would return the first formatter for every later call.
    const d = new Date(Date.UTC(2026, 8, 3));
    const short = formatDate(d, "en-IE", {
      dateStyle: "short",
      timeZone: "UTC",
    });
    const long = formatDate(d, "en-IE", { dateStyle: "long", timeZone: "UTC" });
    expect(short).not.toBe(long);
    expect(long).toContain("September");
    // And the first one is still correct after the second was built.
    expect(
      formatDate(d, "en-IE", { dateStyle: "short", timeZone: "UTC" }),
    ).toBe(short);
  });
});
