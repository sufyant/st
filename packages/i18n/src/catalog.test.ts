import { describe, expect, it, vi } from "vitest";
import { catalogChain, deepMerge, resolveCatalog } from "./catalog.ts";

describe("catalogChain", () => {
  it("puts the language before the region, so the region wins", () => {
    expect(catalogChain("en-IE")).toEqual(["en", "en-IE"]);
  });

  it("does not repeat a bare language", () => {
    expect(catalogChain("en")).toEqual(["en"]);
  });

  it("keeps a script subtag with the region rather than splitting on it", () => {
    // The reason languageOf parses with Intl.Locale instead of slicing at the
    // first hyphen: this would be ["zh", "Hant"] otherwise.
    expect(catalogChain("zh-Hant-TW")).toEqual(["zh", "zh-Hant-TW"]);
  });
});

describe("deepMerge", () => {
  it("keeps sibling keys the later layer does not mention", () => {
    // The whole point of a regional catalog being a layer: overriding one
    // string must not drop the rest of its namespace.
    expect(
      deepMerge([
        { auth: { signIn: "Sign in", signOut: "Sign out" } },
        { auth: { signIn: "Sign in (GB)" } },
      ]),
    ).toEqual({ auth: { signIn: "Sign in (GB)", signOut: "Sign out" } });
  });

  it("merges namespaces that only one layer has", () => {
    expect(deepMerge([{ a: { x: 1 } }, { b: { y: 2 } }])).toEqual({
      a: { x: 1 },
      b: { y: 2 },
    });
  });

  it("replaces arrays instead of merging them element by element", () => {
    // A translated list of two items must not inherit a third from a longer
    // base list.
    expect(deepMerge([{ a: ["x", "y", "z"] }, { a: ["p"] }])).toEqual({
      a: ["p"],
    });
  });

  it("does not mutate its inputs", () => {
    const base = { auth: { signIn: "Sign in" } };
    deepMerge([base, { auth: { signIn: "changed" } }]);
    expect(base.auth.signIn).toBe("Sign in");
  });
});

describe("resolveCatalog", () => {
  const base = { common: { retry: "Try again", home: "Home" } };
  const gb = { common: { home: "Home (GB)" } };

  const catalogs = {
    en: async () => ({ default: base }),
    "en-GB": async () => ({ default: gb }),
  };

  it("layers a region catalog over its language", async () => {
    await expect(resolveCatalog(catalogs, "en-GB", "en", "t")).resolves.toEqual(
      { common: { retry: "Try again", home: "Home (GB)" } },
    );
  });

  it("uses the language alone when the region has no catalog", async () => {
    await expect(resolveCatalog(catalogs, "en-IE", "en", "t")).resolves.toEqual(
      base,
    );
  });

  it("falls back to the fallback language, loudly", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    await expect(resolveCatalog(catalogs, "tr-TR", "en", "t")).resolves.toEqual(
      base,
    );
    expect(warn).toHaveBeenCalledOnce();
    warn.mockRestore();
  });

  it("throws when even the fallback is missing, rather than returning {}", async () => {
    // An empty catalog renders every label as a raw key. Failing is the
    // smaller problem and the one someone will notice.
    await expect(resolveCatalog(catalogs, "tr-TR", "de", "t")).rejects.toThrow(
      /no catalog/,
    );
  });
});
