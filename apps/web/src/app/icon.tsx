import { theme } from "@st/tokens";
import { ImageResponse } from "next/og";

export const size = { width: 32, height: 32 };
export const contentType = "image/png";

/**
 * Generated rather than committed as a .png or .svg, for one reason: a static
 * file would carry a hex code, and a color exists in exactly one place in this
 * repo. Change the brand in packages/tokens/src/theme.ts and the favicon
 * follows on the next build.
 *
 * Rendered once at build time — this is not a per-request cost.
 *
 * A tab icon is theme-independent (browsers do not re-request it when the
 * device flips to dark), so the light palette is used and the mark is drawn to
 * read on either chrome.
 */
export default function Icon() {
  return new ImageResponse(
    <div
      style={{
        width: "100%",
        height: "100%",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        background: theme.light.primary,
        color: theme.light.primaryForeground,
        fontSize: 20,
        fontWeight: 700,
        letterSpacing: "-0.05em",
        borderRadius: 7,
      }}
    >
      st
    </div>,
    size,
  );
}
