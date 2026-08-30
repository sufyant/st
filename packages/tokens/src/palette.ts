/**
 * Raw palette — the only file in this repo where a literal color value appears.
 *
 * Names follow Tailwind's scale so a value can be cross-checked against a
 * designer's reference without a conversion step. Nothing here carries meaning;
 * meaning is assigned in theme.ts. Never import this file from a component:
 * a component that names `indigo` cannot be re-themed.
 */
export const palette = {
  white: "#ffffff",
  transparent: "transparent",

  neutral: {
    50: "#fafafa",
    100: "#f5f5f5",
    200: "#e5e5e5",
    300: "#d4d4d4",
    400: "#a1a1a1",
    500: "#737373",
    600: "#525252",
    700: "#404040",
    800: "#262626",
    900: "#171717",
    950: "#0a0a0a",
  },

  /** Brand. Indigo rather than blue, which is the link color, and rather than
   *  green, which is the success color. */
  indigo: {
    400: "#818cf8",
    600: "#4f46e5",
    950: "#1e1b4b",
  },

  green: { 400: "#4ade80", 700: "#15803d", 950: "#052e16" },
  amber: { 400: "#fbbf24", 700: "#b45309", 950: "#451a03" },
  red: { 400: "#f87171", 600: "#dc2626", 950: "#450a0a" },

  /** White at low alpha, for borders that sit on a dark surface. */
  whiteAlpha: { 10: "#ffffff1a", 15: "#ffffff26" },
} as const;
