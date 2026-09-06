"use client";

import { useEffect } from "react";

export default function GlobalError({
  error,
  retry,
}: {
  error: Error & { digest?: string };
  retry: () => void;
}) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <html lang="en">
      <body
        style={{
          display: "flex",
          minHeight: "100vh",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          gap: "0.5rem",
          fontFamily: "system-ui, sans-serif",
          textAlign: "center",
        }}
      >
        <title>Something went wrong | ST</title>
        <h1 style={{ fontSize: "1.5rem", fontWeight: 600 }}>
          Something went wrong
        </h1>
        <p style={{ color: "#6b7280" }}>
          An unexpected error occurred. You can try again.
        </p>
        <button
          type="button"
          onClick={() => retry()}
          style={{
            marginTop: "1rem",
            padding: "0.5rem 1rem",
            borderRadius: "0.5rem",
            border: "none",
            background: "#111827",
            color: "#fff",
            cursor: "pointer",
          }}
        >
          Try again
        </button>
      </body>
    </html>
  );
}
