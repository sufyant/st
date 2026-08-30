import { palette as p } from "./palette.ts";

/**
 * Semantic tokens: what a color *means*, resolved per theme.
 *
 * Components reference these names, never the palette, which is what makes
 * re-theming a change to this one file.
 *
 * Every solid surface has a paired foreground, and the pair is chosen so it
 * clears WCAG AA (4.5:1) in its own theme. Measured ratios are noted inline.
 * `scripts/check-contrast.ts` re-measures them; run it after changing a value.
 */
export const theme = {
  light: {
    background: p.white,
    foreground: p.neutral[950],
    card: p.white,
    cardForeground: p.neutral[950],
    popover: p.white,
    popoverForeground: p.neutral[950],

    primary: p.indigo[600], // on primaryForeground — 6.29:1
    primaryForeground: p.white,
    secondary: p.neutral[100],
    secondaryForeground: p.neutral[900],
    muted: p.neutral[100],
    mutedForeground: p.neutral[500],
    accent: p.neutral[100],
    accentForeground: p.neutral[900],

    destructive: p.red[600], // 4.83:1
    destructiveForeground: p.white,
    success: p.green[700], // 5.02:1 — green-600 would fail at 3.30:1
    successForeground: p.white,
    warning: p.amber[700], // 5.02:1 — amber-600 would fail at 3.19:1
    warningForeground: p.white,

    border: p.neutral[200],
    input: p.neutral[200],
    ring: p.indigo[600],

    chart1: p.neutral[300],
    chart2: p.neutral[500],
    chart3: p.neutral[600],
    chart4: p.neutral[700],
    chart5: p.neutral[800],

    sidebar: p.neutral[50],
    sidebarForeground: p.neutral[950],
    sidebarPrimary: p.indigo[600],
    sidebarPrimaryForeground: p.white,
    sidebarAccent: p.neutral[100],
    sidebarAccentForeground: p.neutral[900],
    sidebarBorder: p.neutral[200],
    sidebarRing: p.indigo[600],
  },

  dark: {
    background: p.neutral[950],
    foreground: p.neutral[50],
    card: p.neutral[900],
    cardForeground: p.neutral[50],
    popover: p.neutral[900],
    popoverForeground: p.neutral[50],

    // Dark mode lightens the brand and darkens its text. Reusing indigo-600
    // here, or indigo-500 with white text, lands at 4.47:1 and fails.
    primary: p.indigo[400], // 5.36:1
    primaryForeground: p.indigo[950],
    secondary: p.neutral[800],
    secondaryForeground: p.neutral[50],
    muted: p.neutral[800],
    mutedForeground: p.neutral[400],
    accent: p.neutral[800],
    accentForeground: p.neutral[50],

    destructive: p.red[400], // 5.84:1 — with white text this is 2.89:1
    destructiveForeground: p.red[950],
    success: p.green[400], // 8.55:1
    successForeground: p.green[950],
    warning: p.amber[400], // 8.97:1
    warningForeground: p.amber[950],

    border: p.whiteAlpha[10],
    input: p.whiteAlpha[15],
    ring: p.indigo[400],

    chart1: p.neutral[300],
    chart2: p.neutral[500],
    chart3: p.neutral[600],
    chart4: p.neutral[700],
    chart5: p.neutral[800],

    sidebar: p.neutral[900],
    sidebarForeground: p.neutral[50],
    sidebarPrimary: p.indigo[400],
    sidebarPrimaryForeground: p.indigo[950],
    sidebarAccent: p.neutral[800],
    sidebarAccentForeground: p.neutral[50],
    sidebarBorder: p.whiteAlpha[10],
    sidebarRing: p.indigo[400],
  },
} as const;

/** Not a color, but a token all the same: shared by web and native. */
export const radius = "0.625rem";

export type ThemeName = keyof typeof theme;
export type TokenName = keyof (typeof theme)["light"];

/**
 * Foreground pairs that must clear AA. Anything solid enough to put text on
 * belongs here; muted/border/ring are decorative and are checked by eye.
 */
export const contrastPairs = [
  ["background", "foreground"],
  ["card", "cardForeground"],
  ["popover", "popoverForeground"],
  ["primary", "primaryForeground"],
  ["secondary", "secondaryForeground"],
  ["accent", "accentForeground"],
  ["destructive", "destructiveForeground"],
  ["success", "successForeground"],
  ["warning", "warningForeground"],
  ["sidebar", "sidebarForeground"],
  ["sidebarPrimary", "sidebarPrimaryForeground"],
] as const satisfies ReadonlyArray<readonly [TokenName, TokenName]>;
