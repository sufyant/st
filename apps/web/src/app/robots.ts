import type { MetadataRoute } from "next";

/**
 * This app sits behind authentication and must never be indexed. Only
 * apps/marketing is a public surface; a dashboard URL in search results leaks
 * structure and invites traffic that can only ever get a sign-in page.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", disallow: "/" },
  };
}
