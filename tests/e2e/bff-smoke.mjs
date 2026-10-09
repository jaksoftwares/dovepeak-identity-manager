#!/usr/bin/env node
// End-to-end smoke test for the Next.js BFF example (milestone M1.2).
//
// Drives the real flow over HTTP: register through the app -> verify email (Mailpit) -> session established ->
// protected API call -> CSRF-protected logout. Requires the local stack, `dovepeak-dev demo-setup`,
// the protected API and the BFF (`npm run dev`) to be running.
//
// Usage: node tests/e2e/bff-smoke.mjs

const APP = process.env.APP_URL ?? "http://localhost:3000";
const MAILPIT = process.env.MAILPIT_URL ?? "http://localhost:8025";

const cookies = new Map();
const steps = [];

function storeCookies(response) {
  for (const header of response.headers.getSetCookie()) {
    const [pair] = header.split(";");
    const index = pair.indexOf("=");
    const name = pair.slice(0, index).trim();
    const value = pair.slice(index + 1).trim();
    if (!value || /max-age=0|expires=thu, 01 jan 1970/i.test(header)) cookies.delete(name);
    else cookies.set(name, value);
  }
}

async function request(url, init = {}) {
  const headers = new Headers(init.headers);
  if (cookies.size) headers.set("cookie", [...cookies].map(([k, v]) => `${k}=${v}`).join("; "));
  const response = await fetch(url, { ...init, headers, redirect: "manual" });
  storeCookies(response);
  return response;
}

/** Follows redirects, carrying cookies, until a non-redirect response. */
async function follow(url, init = {}) {
  let response = await request(url, init);
  for (let hops = 0; hops < 10 && response.status >= 300 && response.status < 400; hops++) {
    url = new URL(response.headers.get("location"), url).toString();
    response = await request(url);
  }
  return { response, url };
}

function formAction(html, formId, baseUrl) {
  const match = html.match(new RegExp(`<form[^>]*id="${formId}"[^>]*action="([^"]+)"`));
  if (!match) throw new Error(`form ${formId} not found`);
  return new URL(match[1].replaceAll("&amp;", "&"), baseUrl).toString();
}

async function waitForEmail(to, subject) {
  for (let attempt = 0; attempt < 80; attempt++) {
    const search = await fetch(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}`).then((r) => r.json());
    const message = search.messages?.find((m) => m.Subject.includes(subject));
    if (message) return fetch(`${MAILPIT}/api/v1/message/${message.ID}`).then((r) => r.json());
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`no "${subject}" email for ${to}`);
}

function check(name, condition, detail = "") {
  steps.push({ name, ok: Boolean(condition) });
  console.log(`${condition ? "PASS" : "FAIL"}  ${name}${detail ? `  (${detail})` : ""}`);
  if (!condition) process.exitCode = 1;
}

const email = `bff-${crypto.randomUUID()}@example.test`;
const password = `Bff-${crypto.randomUUID()}`;

// 1. Registration through the app.
const registration = await follow(`${APP}/api/auth/login?register=1`);
const registrationHtml = await registration.response.text();
check("app redirects to hosted registration page", registrationHtml.includes("kc-register-form"));

const afterRegister = await follow(formAction(registrationHtml, "kc-register-form", registration.url), {
  method: "POST",
  body: new URLSearchParams({ email, password, "password-confirm": password }),
});
check("registration requires email verification", /verify your email/i.test(await afterRegister.response.text()));

// 2. Email verification completes sign-in and returns to the app.
// Matches both Keycloak's default subject and the Dovepeak email theme's ("Verify your email address").
const mail = await waitForEmail(email, "Verify");
const link = mail.HTML.match(/href="([^"]*\/login-actions\/action-token[^"]*)"/)[1].replaceAll("&amp;", "&");
const landed = await follow(link);
check("verification link returns to the app", landed.url.startsWith(APP), landed.url);
check("session cookie is set", cookies.has("dp_session"));

// 3. Session and token handling.
const me = await request(`${APP}/api/me`);
const meText = await me.text();
const meBody = JSON.parse(meText);
check("/api/me reports the signed-in user", me.status === 200 && meBody.user?.email === email);
check("no tokens are exposed to the browser", !meText.includes("eyJ"));

const api = await request(`${APP}/api/protected`);
const apiBody = await api.json();
check("protected API accepts the access token", api.status === 200 && apiBody.subject === meBody.user.sub, `status ${api.status}`);
check("access token carries the client identity", apiBody.clientId === "demo-bff");

// 4. Logout.
const csrf = await request(`${APP}/api/auth/logout`, { method: "POST", headers: { origin: "https://attacker.example" } });
check("cross-site logout is rejected", csrf.status === 403);

const logout = await request(`${APP}/api/auth/logout`, { method: "POST", headers: { origin: APP } });
check("logout redirects to end the identity provider session", logout.status === 303 && logout.headers.get("location").includes("/logout"));
check("session cookie is cleared", !cookies.has("dp_session"));
const afterLogout = await request(`${APP}/api/me`);
check("app no longer recognises the session", afterLogout.status === 401);

const failed = steps.filter((s) => !s.ok).length;
console.log(`\n${steps.length - failed}/${steps.length} checks passed`);
