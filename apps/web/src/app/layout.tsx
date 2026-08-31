import { theme } from "@st/tokens";
import { ThemeProvider } from "@st/ui/components/theme-provider";
import { cn } from "@st/ui/lib/utils";
import type { Metadata, Viewport } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { NextIntlClientProvider } from "next-intl";
import { AuthProvider } from "@/lib/auth/components";
// Imported for its side effect: parsing the environment here means a bad value
// stops the build, not a request.
import "@/lib/env";
import { resolveLocale } from "@/lib/locale";
import "./globals.css";

const fontSans = Geist({ subsets: ["latin"], variable: "--font-sans" });
const fontMono = Geist_Mono({ subsets: ["latin"], variable: "--font-mono" });

export const metadata: Metadata = {
  title: {
    // TODO: product name not decided yet — "st" is the workspace name, not a brand.
    default: "st",
    template: "%s · st",
  },
};

/**
 * Not metadata, and not cosmetic either.
 *
 * `colorScheme` is what tells the browser to render its own widgets — form
 * controls, scrollbars, the spellcheck underline — in dark. Without it the
 * page turns dark and a native <select> stays white. `themeColor` is the
 * mobile address bar, which otherwise sits in the wrong shade above a dark
 * page.
 *
 * Both colors are read from @st/tokens rather than written here. A hex in this
 * file would be a second place the brand lives, and the one that nobody
 * updates.
 */
export const viewport: Viewport = {
  colorScheme: "light dark",
  themeColor: [
    {
      media: "(prefers-color-scheme: light)",
      color: theme.light.background,
    },
    { media: "(prefers-color-scheme: dark)", color: theme.dark.background },
  ],
};

export default async function RootLayout({ children }: LayoutProps<"/">) {
  // Resolved once, here, and handed down. `lang` is not decoration: it is what
  // a screen reader picks a voice from and what the browser offers to
  // translate, so hardcoding it survives right up until the second language.
  const locale = await resolveLocale();

  return (
    <html
      lang={locale}
      suppressHydrationWarning
      className={cn("antialiased", fontSans.variable, fontMono.variable)}
    >
      <body>
        <NextIntlClientProvider>
          <AuthProvider locale={locale}>
            <ThemeProvider>{children}</ThemeProvider>
          </AuthProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
