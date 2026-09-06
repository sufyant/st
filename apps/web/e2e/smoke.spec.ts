import { expect, test } from "@playwright/test";

test("unauthenticated visitors are redirected to sign in", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveURL(/\/sign-in/);
  await expect(page.getByRole("textbox", { name: /email/i })).toBeVisible();
});
