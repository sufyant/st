"use client";

import { Button } from "@st/ui/components/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@st/ui/components/dropdown-menu";
import { Moon, Sun } from "lucide-react";
import { useTranslations } from "next-intl";
import { useTheme } from "next-themes";

const OPTIONS = ["light", "dark", "system"] as const;

/**
 * App-local by the promotion rule: generic-looking, but only one app needs it
 * so far. It moves to @st/ui when apps/admin actually asks for it.
 *
 * Three choices, not a two-way switch. `system` is the default, and a toggle
 * that only flips light/dark is a one-way door out of it: the first click
 * writes an explicit preference and nothing can ever clear it again. Wanting
 * dark on a phone and light on a desktop is correct behavior, so following the
 * device has to stay reachable.
 *
 * That is also why this is a menu rather than a cycling button. With three
 * states the control has to show which one is active, and reading `theme` to
 * render the trigger would differ between the server's HTML and the browser's.
 * A menu sidesteps it: the trigger's icon comes from CSS, and the current
 * value is only read once the menu is open, which is always client-side.
 */
export function ThemeToggle() {
  const t = useTranslations("common.theme");
  const { theme, setTheme } = useTheme();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button variant="outline" size="icon" aria-label={t("label")}>
            <Sun className="hidden dark:block" />
            <Moon className="block dark:hidden" />
          </Button>
        }
      />
      {/*
        The vendored content is `w-(--anchor-width)`, which matches a menu to
        its trigger — right for a select-shaped button, wrong here: the anchor
        is a 32px icon, so every label wraps. Overridden at the call site
        rather than in @st/ui, because the default is correct for the triggers
        it was written for.
      */}
      <DropdownMenuContent align="end" className="w-fit">
        <DropdownMenuRadioGroup value={theme} onValueChange={setTheme}>
          {OPTIONS.map((option) => (
            <DropdownMenuRadioItem key={option} value={option}>
              {t(option)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
