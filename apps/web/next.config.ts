import path from "node:path";
import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

/**
 * Headers applied to every response.
 *
 * These are the protections that cannot be expressed in application code: a
 * page cannot refuse to be framed, and a crawler never runs our React. They
 * belong here rather than in the proxy so they also cover static assets and
 * responses the proxy's matcher skips.
 */
const securityHeaders = [
  /**
   * Clickjacking. `frame-ancestors` is the modern rule and the one browsers
   * honor; X-Frame-Options is kept for the older clients that only understand
   * that. This is the only CSP directive set — a script-src policy needs
   * per-request nonces plumbed through the layout, and a half-written one that
   * everybody disables is worse than none.
   */
  { key: "Content-Security-Policy", value: "frame-ancestors 'none'" },
  { key: "X-Frame-Options", value: "DENY" },

  /**
   * `same-origin` would be stronger, and would also break Clerk's Google
   * sign-in: that flow opens a popup and talks back through `window.opener`,
   * which `same-origin` severs. Google OAuth is enabled on the instance —
   * verified with `clerk config pull` — so the popup-tolerant variant is the
   * correct one, not a concession.
   */
  { key: "Cross-Origin-Opener-Policy", value: "same-origin-allow-popups" },

  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "X-DNS-Prefetch-Control", value: "off" },
  {
    key: "Permissions-Policy",
    value: "camera=(), microphone=(), geolocation=(), payment=()",
  },

  /**
   * robots.txt is a request; this is an instruction on every response, and it
   * covers URLs a crawler reached without ever reading robots.txt. The app
   * must never be indexed — see the Indexing section of AGENTS.md.
   */
  { key: "X-Robots-Tag", value: "noindex, nofollow" },

  /**
   * Ignored by browsers over plain http, so this is inert in local
   * development and active the moment the app is served over TLS. Deliberately
   * without `preload`: that submits the domain to a browser-shipped list and
   * is effectively irreversible, which is a decision to make on purpose rather
   * than inherit from a config file.
   */
  {
    key: "Strict-Transport-Security",
    value: "max-age=63072000; includeSubDomains",
  },
];

const nextConfig: NextConfig = {
  /**
   * Azure Container Apps runs this as a container. `standalone` emits
   * .next/standalone with a server.js and only the traced subset of
   * node_modules, which is the difference between an image that ships the
   * workspace's dependency tree and one that ships what the app actually
   * imports.
   */
  output: "standalone",

  /**
   * Tracing defaults to the app directory, which in a workspace stops before
   * the symlinked packages. Without this, @st/ui and @st/tokens are traced as
   * far as their symlink and the container starts and then fails on a missing
   * module.
   */
  outputFileTracingRoot: path.join(import.meta.dirname, "../.."),

  /** Announces the framework and version to anyone scanning. No upside. */
  poweredByHeader: false,

  headers() {
    return Promise.resolve([{ source: "/:path*", headers: securityHeaders }]);
  },
};

/**
 * Without the plugin, `getLocale` and `getTranslations` have nothing to read on
 * the server. It finds `src/i18n/request.ts` by convention.
 */
const withNextIntl = createNextIntlPlugin();

export default withNextIntl(nextConfig);
