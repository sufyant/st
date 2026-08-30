/**
 * Emits tokens.css from the TypeScript source.
 *
 * Output is written in Biome's CSS style on purpose: the file is committed, so
 * `pnpm format` sees it. If the two ever disagree, formatting and `--check`
 * fight each other forever.
 *
 * The generated file is committed so no app needs a build step to consume the
 * package. `--check` re-generates in memory and exits non-zero on a difference,
 * which is what CI runs to catch an edited source with a stale output.
 */
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { radius, theme } from "../src/theme.ts";
import { hexToOklch } from "./color.ts";

const OUT = join(dirname(fileURLToPath(import.meta.url)), "..", "tokens.css");

const kebab = (name: string) =>
  name.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`).replace(/(\d+)$/, "-$1");

/** Colors only — every value here goes through the hex parser, which rejects
 *  anything that is not a color. Non-color tokens are appended separately. */
function block(selector: string, tokens: Record<string, string>): string {
  const lines = Object.entries(tokens).map(
    ([name, hex]) => `  --${kebab(name)}: ${hexToOklch(hex)};`,
  );
  return `${selector} {\n${lines.join("\n")}\n}`;
}

const css = `/*
 * GENERATED FILE — do not edit.
 * Source: packages/tokens/src/theme.ts
 * Regenerate: pnpm --filter @st/tokens generate
 *
 * Values are authored as hex and emitted as oklch, which is the space the rest
 * of the styling layer speaks. Editing this file directly is lost on the next
 * regeneration and drifts the web away from native, which reads the same source.
 */

${block(":root", theme.light).replace(/\n}$/, `\n  --radius: ${radius};\n}`)}

${block(".dark", theme.dark)}
`;

const isCheck = process.argv.includes("--check");

if (isCheck) {
  const existing = readFileSync(OUT, "utf8");
  if (existing !== css) {
    console.error(
      "tokens.css is stale — src/theme.ts changed without regenerating.\n" +
        "Run: pnpm --filter @st/tokens generate",
    );
    process.exit(1);
  }
  console.log("tokens.css is current.");
} else {
  writeFileSync(OUT, css);
  console.log(`Wrote ${OUT}`);
}
