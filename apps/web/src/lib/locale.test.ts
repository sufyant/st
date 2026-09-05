import { describe, expect, it } from "vitest";
import { DEFAULT_LOCALE, isLocale, LOCALES, languageOf } from "./locale";

describe("locale", () => {
  it("has no bare language in LOCALES", () => {
    // `en` alone silently formats the American way. The region is not optional.
    for (const locale of LOCALES) {
      expect(languageOf(locale)).not.toBe(locale);
    }
  });

  it("treats the default as one of the supported locales", () => {
    expect(isLocale(DEFAULT_LOCALE)).toBe(true);
  });

  it("rejects a locale we do not serve", () => {
    expect(isLocale("en-US")).toBe(false);
  });
});

describe("languageOf", () => {
  it("returns the language subtag", () => {
    expect(languageOf("en-IE")).toBe("en");
    expect(languageOf("en")).toBe("en");
  });

  it("does not mistake a script subtag for a region", () => {
    expect(languageOf("zh-Hant-TW")).toBe("zh");
  });
});
