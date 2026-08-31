"use client";

import messages from "@st/i18n/messages/en.json";

/**
 * The boundary for an error in the root layout itself.
 *
 * It replaces the layout, which means no provider, no fonts, no theme class
 * and no globals.css — anything this page needs, it has to bring. That rules
 * out `useTranslations` (the provider is gone) and Tailwind classes (the
 * stylesheet may be what failed), so the copy comes from the static English
 * catalog and the styling is inline.
 *
 * This is the page for when the machinery is broken. Making it depend on the
 * machinery would be the one mistake it cannot survive.
 */
export default function GlobalError() {
  return (
    <html lang="en">
      <body
        style={{
          display: "flex",
          minHeight: "100vh",
          alignItems: "center",
          justifyContent: "center",
          margin: 0,
          fontFamily: "system-ui, sans-serif",
          textAlign: "center",
        }}
      >
        <main style={{ maxWidth: "28rem", padding: "2rem" }}>
          <h1 style={{ fontSize: "1.5rem", fontWeight: 600 }}>
            {messages.common.unexpected.title}
          </h1>
          <p style={{ opacity: 0.7 }}>{messages.common.unexpected.body}</p>
        </main>
      </body>
    </html>
  );
}
