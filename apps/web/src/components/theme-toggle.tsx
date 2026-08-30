"use client";

import { Button } from "@st/ui/components/button";
import { Moon, Sun } from "lucide-react";
import { useTheme } from "next-themes";

/**
 * App-local by the promotion rule: generic-looking, but only one app needs it
 * so far. It moves to @st/ui when apps/admin actually asks for it.
 */
export function ThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme();

  return (
    <Button
      variant="outline"
      size="icon"
      aria-label="Toggle theme"
      onClick={() => setTheme(resolvedTheme === "dark" ? "light" : "dark")}
    >
      <Sun className="hidden dark:block" />
      <Moon className="block dark:hidden" />
    </Button>
  );
}
