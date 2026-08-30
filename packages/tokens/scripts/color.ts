/**
 * Color math for the build scripts. Not part of the package's public surface —
 * consumers get finished values, not the means to compute them.
 */

type Rgb = { r: number; g: number; b: number; a: number };

export function parseHex(hex: string): Rgb {
  if (!/^#(?:[0-9a-f]{6}|[0-9a-f]{8})$/i.test(hex)) {
    // Without this, a length like "0.625rem" parses to garbage and ships as a
    // plausible-looking color. Fail loudly instead.
    throw new Error(`Not a hex color: ${JSON.stringify(hex)}`);
  }
  const h = hex.replace("#", "");
  const n = (i: number) => Number.parseInt(h.slice(i, i + 2), 16);
  return {
    r: n(0),
    g: n(2),
    b: n(4),
    a: h.length === 8 ? n(6) / 255 : 1,
  };
}

const toLinear = (c: number) => {
  const v = c / 255;
  return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
};

/** CSS `oklch(...)`, with an alpha suffix only when the color has one. */
export function hexToOklch(hex: string): string {
  const { r, g, b, a } = parseHex(hex);
  const [lr, lg, lb] = [toLinear(r), toLinear(g), toLinear(b)];

  const l = 0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb;
  const m = 0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb;
  const s = 0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb;
  const [l_, m_, s_] = [Math.cbrt(l), Math.cbrt(m), Math.cbrt(s)];

  const L = 0.2104542553 * l_ + 0.793617785 * m_ - 0.0040720468 * s_;
  const A = 1.9779984951 * l_ - 2.428592205 * m_ + 0.4505937099 * s_;
  const B = 0.0259040371 * l_ + 0.7827717662 * m_ - 0.808675766 * s_;

  const chroma = Math.hypot(A, B);
  const hue =
    chroma < 1e-6 ? 0 : ((Math.atan2(B, A) * 180) / Math.PI + 360) % 360;
  const round = (v: number, d: number) => Number(v.toFixed(d));

  const base = `${round(L, 3)} ${round(chroma, 3)} ${round(hue, 3)}`;
  return a < 1 ? `oklch(${base} / ${round(a * 100, 0)}%)` : `oklch(${base})`;
}

/** Relative luminance per WCAG 2.1. Alpha is ignored — a translucent color has
 *  no fixed contrast, which is why only opaque pairs are ever checked. */
function luminance(hex: string): number {
  const { r, g, b } = parseHex(hex);
  return 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b);
}

export function contrast(a: string, b: string): number {
  const [x, y] = [luminance(a), luminance(b)];
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
}
