import { expect, test } from "@playwright/test";

const MAILPIT = process.env.MAILPIT_URL ?? "http://localhost:8025";

async function verificationLink(to: string): Promise<string> {
  for (let attempt = 0; attempt < 120; attempt++) {
    const search = await fetch(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}`).then((r) => r.json());
    const message = search.messages?.find((m: { Subject: string }) => m.Subject.includes("Verify"));
    if (message) {
      const mail = await fetch(`${MAILPIT}/api/v1/message/${message.ID}`).then((r) => r.json());
      return mail.HTML.match(/href="([^"]*\/login-actions\/action-token[^"]*)"/)[1].replaceAll("&amp;", "&");
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`No verification email for ${to}`);
}

test("a user signs up, calls the API with an in-memory token and signs out", async ({ page }) => {
  const email = `spa-${crypto.randomUUID()}@example.test`;
  const password = `Spa-${crypto.randomUUID()}`;

  await page.goto("/");
  await page.getByRole("button", { name: "Create account" }).click();
  await page.locator("#email").fill(email);
  await page.locator("#password").fill(password);
  await page.locator("#password-confirm").fill(password);
  await page.locator("#kc-register-form [type=submit]").click();
  await expect(page.getByText(/verify your email/i).first()).toBeVisible();

  // The verification link returns to /callback in the same tab, where the PKCE transaction is waiting.
  await page.goto(await verificationLink(email));
  await expect(page.getByTestId("signed-in")).toContainText(email);
  await expect(page).toHaveURL(/localhost:5173\/$/);

  // Tokens live in memory only: nothing in localStorage, and the one-time transaction is gone.
  expect(await page.evaluate(() => localStorage.length)).toBe(0);
  expect(await page.evaluate(() => sessionStorage.length)).toBe(0);

  await page.getByRole("button", { name: "Call protected API" }).click();
  await expect(page.getByTestId("api-result")).toContainText("200");
  await expect(page.getByTestId("api-result")).toContainText(`"clientId": "demo-spa"`);

  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
});
