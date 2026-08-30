/**
 * Fails if any surface/foreground pair drops below WCAG AA (4.5:1) in either
 * theme. Contrast is the kind of regression nobody notices in review — a palette
 * tweak that reads fine on the author's monitor can put a button below the line
 * for everyone else.
 */
import { contrastPairs, theme } from "../src/theme.ts";
import { contrast } from "./color.ts";

const AA = 4.5;
let failed = 0;

for (const [themeName, tokens] of Object.entries(theme)) {
  console.log(`\n${themeName}`);
  for (const [surface, foreground] of contrastPairs) {
    const ratio = contrast(tokens[surface], tokens[foreground]);
    const ok = ratio >= AA;
    if (!ok) failed++;
    console.log(
      `  ${ok ? "pass" : "FAIL"}  ${ratio.toFixed(2).padStart(5)}:1  ${surface} / ${foreground}`,
    );
  }
}

if (failed > 0) {
  console.error(`\n${failed} pair(s) below AA (${AA}:1).`);
  process.exit(1);
}
console.log(`\nAll pairs clear AA (${AA}:1).`);
