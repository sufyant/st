/**
 * Design tokens, platform-neutral.
 *
 * Web consumes the generated `tokens.css`; native imports these values directly.
 * Both come from `theme.ts`, so a color is never defined twice.
 */
export { palette } from "./palette.ts";
export type { ThemeName, TokenName } from "./theme.ts";
export { contrastPairs, radius, theme } from "./theme.ts";
