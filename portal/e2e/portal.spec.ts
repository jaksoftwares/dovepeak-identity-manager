import { expect, test, type Page } from "@playwright/test";

// The developer journey through the portal, end to end: account creation with email verification, organization,
// project provisioning by the workers, scopes, applications, one-time secrets, overlapping rotation, token policy,
// members, API keys, webhooks, audit log, documentation, clean-up and sign-out. Credentials issued by the portal
// are exercised directly against the identity engine and the Management API.

const MAILPIT = process.env.MAILPIT_URL ?? "http://localhost:8025";
const API = process.env.MANAGEMENT_API_URL ?? "http://localhost:5080";

const suffix = crypto.randomUUID().replaceAll("-", "").slice(0, 10);
const developer = { email: `portal-${suffix}@example.test`, password: `Portal-${crypto.randomUUID()}` };
const orgName = `Portal ${suffix}`;

async function verificationLink(to: string): Promise<string> {
  for (let attempt = 0; attempt < 120; attempt++) {
    const search = await fetch(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}`).then((r) => r.json());
    const message = search.messages?.find((m: { Subject: string }) => m.Subject.includes("Verify your email"));
    if (message) {
      const mail = await fetch(`${MAILPIT}/api/v1/message/${message.ID}`).then((r) => r.json());
      return mail.HTML.match(/href="([^"]*\/login-actions\/action-token[^"]*)"/)[1].replaceAll("&amp;", "&");
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`No verification email for ${to}`);
}

async function clientCredentials(issuer: string, clientId: string, secret: string, scope?: string) {
  const body = new URLSearchParams({ grant_type: "client_credentials", client_id: clientId, client_secret: secret, ...(scope && { scope }) });
  const response = await fetch(`${issuer}/protocol/openid-connect/token`, { method: "POST", body });
  return { status: response.status, json: (await response.json()) as Record<string, unknown> };
}

async function oneTimeSecret(page: Page): Promise<string> {
  const value = page.getByTestId("secret-value");
  await expect(value).toBeVisible();
  return (await value.textContent())!.trim();
}

async function success(page: Page, text: string | RegExp) {
  await expect(page.getByRole("status").filter({ hasText: text }).first()).toBeVisible();
}

test("a new developer onboards an application end to end", async ({ page, context }) => {
  // ---------------------------------------------------------------- Account and sign-in
  await page.goto("/");
  await page.getByRole("link", { name: "Create an account" }).click();
  await page.locator("#email").fill(developer.email);
  await page.locator("#password").fill(developer.password);
  await page.locator("#password-confirm").fill(developer.password);
  await page.locator("#kc-register-form [type=submit]").click();
  await expect(page.getByText(/verify your email/i).first()).toBeVisible();

  await page.goto(await verificationLink(developer.email));
  await expect(page).toHaveURL(/\/orgs$/);
  await expect(page.getByTestId("signed-in-as")).toHaveText(developer.email);

  const cookie = (await context.cookies()).find((c) => c.name === "dp_portal");
  expect(cookie?.httpOnly).toBe(true);
  expect(cookie?.sameSite).toBe("Lax");
  expect(await page.content()).not.toContain("eyJ"); // no JWTs in the page

  // ---------------------------------------------------------------- Organization and project
  await page.getByLabel("Name").fill(orgName);
  await page.getByLabel(/^Slug/).fill(`portal-${suffix}`);
  await page.getByRole("button", { name: "Create organization" }).click();
  await expect(page).toHaveURL(/\/orgs\/[0-9a-f-]+$/);
  await expect(page.getByRole("heading", { name: orgName })).toBeVisible();
  const orgUrl = page.url();
  const orgId = orgUrl.split("/orgs/")[1]!;

  await page.getByRole("link", { name: "Projects", exact: true }).click();
  await expect(page).toHaveURL(/\/projects$/);
  await page.getByLabel("Name").fill("Storefront");
  await page.getByLabel(/^Slug/).fill("storefront");
  await page.getByRole("button", { name: "Create project" }).click();
  await expect(page.getByRole("heading", { name: "Storefront" })).toBeVisible();

  // The workers container provisions the three realms asynchronously.
  await expect(async () => {
    await page.reload();
    await expect(page.getByTestId("env-development")).toBeVisible({ timeout: 1000 });
    await expect(page.getByTestId("env-production")).toBeVisible({ timeout: 1000 });
  }).toPass({ timeout: 120_000 });
  // ---------------------------------------------------------------- Branding and email templates (all environments)
  const projectUrl = page.url();
  await page.getByRole("link", { name: "Branding and emails" }).click();
  await expect(page).toHaveURL(/\/branding$/);
  await page.getByLabel(/^Logo URL/).fill("https://cdn.example.com/storefront.png");
  await page.getByLabel("Use the Dovepeak colour").uncheck();
  await page.locator("input[name=primaryColor]").fill("#0b5ed7");
  await page.locator("input[name=emailVerificationSubject]").fill("Confirm your Storefront account");
  await page.locator("textarea[name=emailVerificationIntro]").fill("Welcome to Storefront!");
  await expect(page.getByTestId("preview-button")).toHaveCSS("background-color", "rgb(11, 94, 215)");
  await page.getByRole("button", { name: "Save branding" }).click();
  await success(page, "Branding saved");
  await page.reload();
  await expect(page.locator("input[name=emailVerificationSubject]")).toHaveValue("Confirm your Storefront account");
  await expect(page.locator("input[name=primaryColor]")).toHaveValue("#0b5ed7");

  // Unsafe values are refused with an explanation.
  await page.getByLabel(/^Logo URL/).fill("http://cdn.example.com/storefront.png");
  await page.getByRole("button", { name: "Save branding" }).click();
  await expect(page.locator(".alert.error")).toContainText("HTTPS");
  await page.goto(projectUrl);

  await page.getByTestId("env-development").click();
  await expect(page).toHaveURL(/\/environments\/[0-9a-f-]+$/);
  const envUrl = page.url();

  // ---------------------------------------------------------------- Scope and machine application
  await page.getByRole("link", { name: "API scopes" }).click();
  await expect(page).toHaveURL(/\/scopes$/);
  await page.getByLabel("Name").fill("orders:read");
  await page.getByLabel("Description").fill("Read orders");
  await page.getByRole("button", { name: "Create scope" }).click();
  await success(page, "orders:read created");

  await page.goto(envUrl);
  await page.getByLabel("Name").fill("order-sync");
  await page.getByLabel("Type").selectOption("machine");
  await page.getByLabel(/^API audiences/).fill("orders-api");
  await page.getByLabel("orders:read").check();
  await page.getByRole("button", { name: "Create application" }).click();
  const firstSecret = await oneTimeSecret(page);
  const clientId = (await page.getByRole("status").first().textContent())!.match(/app_[a-z2-7]+/)![0];

  await page.getByRole("link", { name: "order-sync" }).click();
  await expect(page).toHaveURL(/\/applications\/[0-9a-f-]+$/);
  await page.getByRole("link", { name: "Integration" }).click();
  await expect(page).toHaveURL(/\/integration$/);
  const issuer = (await page.getByTestId("integration-config").locator("tr", { hasText: "Issuer" }).locator("td").nth(1).textContent())!.trim();
  const appUrl = page.url().replace(/\/integration$/, "");

  const token = await clientCredentials(issuer, clientId, firstSecret, "orders:read");
  expect(token.status).toBe(200);
  expect(String(token.json["scope"])).toContain("orders:read");
  expect(Number(token.json["expires_in"])).toBeGreaterThanOrEqual(598); // Keycloak rounds to the second
  expect(Number(token.json["expires_in"])).toBeLessThanOrEqual(600);

  // ---------------------------------------------------------------- Token policy
  await page.goto(appUrl);
  await page.getByLabel(/^Access token lifetime/).fill("900");
  await page.getByRole("button", { name: "Save settings" }).click();
  await success(page, "Settings saved");
  expect(Number((await clientCredentials(issuer, clientId, firstSecret)).json["expires_in"])).toBeGreaterThanOrEqual(898);

  // ---------------------------------------------------------------- Rotation with overlap, then revocation
  await page.getByRole("link", { name: "Credentials" }).click();
  await expect(page).toHaveURL(/\/credentials$/);
  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Issue a new secret" }).click();
  const secondSecret = await oneTimeSecret(page);
  await success(page, "keeps working until");
  expect(secondSecret).not.toBe(firstSecret);
  expect((await clientCredentials(issuer, clientId, firstSecret)).status).toBe(200);
  expect((await clientCredentials(issuer, clientId, secondSecret)).status).toBe(200);

  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Revoke previous secret" }).click();
  await success(page, "no longer works");
  expect((await clientCredentials(issuer, clientId, firstSecret)).status).toBe(401);
  expect((await clientCredentials(issuer, clientId, secondSecret)).status).toBe(200);

  // Secrets are never shown again.
  await page.reload();
  const html = await page.content();
  expect(html).not.toContain(firstSecret);
  expect(html).not.toContain(secondSecret);

  // ---------------------------------------------------------------- Roles
  await page.getByRole("link", { name: "Roles" }).click();
  await expect(page).toHaveURL(/\/roles$/);
  await page.getByLabel(/^Name/).fill("administrator");
  await page.getByRole("button", { name: "Create role" }).click();
  await success(page, "administrator created");

  // ---------------------------------------------------------------- Members, API keys and webhooks
  await page.goto(`${orgUrl}/members`);
  await page.getByLabel("Email").fill(`colleague-${suffix}@example.test`);
  await page.getByRole("button", { name: "Send invitation" }).click();
  await success(page, "Invitation sent");
  await expect(page.getByRole("cell", { name: `colleague-${suffix}@example.test` })).toBeVisible();

  await page.goto(`${orgUrl}/api-keys`);
  await page.getByLabel("Name").fill("ci-deploy");
  await page.getByRole("button", { name: "Create key" }).click();
  const apiKey = await oneTimeSecret(page);
  expect(apiKey).toMatch(/^dpk_live_/);
  const withKey = () => fetch(`${API}/v1/organizations/${orgId}/projects`, { headers: { Authorization: `Bearer ${apiKey}` } });
  expect((await withKey()).status).toBe(200);
  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Revoke" }).click();
  await expect(page.getByRole("row", { name: /ci-deploy/ })).toContainText("revoked");
  expect((await withKey()).status).toBe(401);

  await page.goto(`${orgUrl}/webhooks`);
  await page.getByLabel(/^Endpoint URL/).fill("https://example.com/hooks/dovepeak");
  await page.getByRole("button", { name: "Add endpoint" }).click();
  expect(await oneTimeSecret(page)).toMatch(/^whsec_/);

  // Unsafe destinations are refused with a clear message.
  await page.getByLabel(/^Endpoint URL/).fill("https://169.254.169.254/latest");
  await page.getByRole("button", { name: "Add endpoint" }).click();
  await expect(page.locator(".alert.error")).toBeVisible();

  // ---------------------------------------------------------------- Audit log and documentation
  await page.goto(`${orgUrl}/audit?source=management`);
  const audit = page.getByTestId("audit-table");
  for (const type of ["organization.created", "application.created", "application.secret_rotated", "api_key.revoked"]) {
    await expect(audit.getByText(type, { exact: true }).first()).toBeVisible();
  }

  await page.goto("/docs/api");
  await expect(page.getByText("/v1/organizations", { exact: true }).first()).toBeVisible();

  // ---------------------------------------------------------------- Clean-up through the portal
  await page.goto(appUrl);
  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Delete application" }).click();
  await expect(page).toHaveURL(envUrl);

  await page.goto(`${orgUrl}/projects`);
  await page.getByRole("link", { name: "Storefront" }).click();
  await expect(page).toHaveURL(/\/projects\/[0-9a-f-]+$/);
  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Delete project" }).click();
  await expect(page).toHaveURL(`${orgUrl}/projects`);

  await page.goto(`${orgUrl}/settings`);
  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Delete organization" }).click();
  await expect(page).toHaveURL(/\/orgs$/);
  await expect(page.getByRole("link", { name: orgName })).toHaveCount(0);

  // ---------------------------------------------------------------- Sign-out ends the session
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("link", { name: "Create an account" })).toBeVisible();
  await page.goto("/orgs");
  await expect(page.locator("#kc-form-login, #kc-page-title").first()).toBeVisible();
});

test("portal pages are protected and send security headers", async ({ request }) => {
  const response = await request.get("/orgs", { maxRedirects: 0 });
  expect(response.status()).toBe(307);
  expect(response.headers()["location"]).toContain("/api/auth/login");

  const home = await request.get("/");
  expect(home.headers()["x-frame-options"]).toBe("DENY");
  // "no-referrer" would make browsers send Origin: null on the sign-out POST and break the CSRF check.
  expect(home.headers()["referrer-policy"]).toBe("same-origin");
  expect(home.headers()["content-security-policy"]).toContain("frame-ancestors 'none'");

  const crossSiteLogout = await request.post("/api/auth/logout", { headers: { Origin: "https://evil.example" }, maxRedirects: 0 });
  expect(crossSiteLogout.status()).toBe(403);
});
