#!/usr/bin/env node
// Keycloak high-availability test for the production deployment (milestone M6.2).
//
// Run by tests/e2e/ha-failover.sh, which stops one Keycloak node between the phases:
//   setup   create a throwaway realm, client and user (admin API of node 1), sign in through Caddy and the edge
//   verify  with node 1 stopped: the refresh token still works and the browser SSO session still signs in silently
//   cleanup delete the realm (admin API of node 2)
//
// Environment: IDENTITY_URL (public, through Caddy), ADMIN_URL_1, ADMIN_URL_2, KEYCLOAK_ADMIN_USERNAME,
// KEYCLOAK_ADMIN_PASSWORD, STATE_FILE.

import { createHash, randomBytes, randomUUID } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";

const env = (name) => process.env[name] ?? (() => { throw new Error(`${name} is not set`); })();
const IDENTITY = env("IDENTITY_URL");
const REDIRECT = "https://app.example.test/callback";
const phase = process.argv[2];
const stateFile = env("STATE_FILE");

const cookies = new Map();
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

/** Follows redirects inside the identity provider; stops at the application's redirect URI. */
async function follow(url, init = {}) {
  let response = await request(url, init);
  while (response.status >= 300 && response.status < 400) {
    url = new URL(response.headers.get("location"), url).toString();
    if (url.startsWith(REDIRECT)) return { url, response };
    response = await request(url);
  }
  return { url, response };
}

function check(name, condition, detail = "") {
  console.log(`${condition ? "PASS" : "FAIL"}  ${name}${detail ? `  (${detail})` : ""}`);
  if (!condition) process.exitCode = 1;
  return condition;
}

async function adminToken(adminUrl) {
  const response = await fetch(`${adminUrl}/realms/master/protocol/openid-connect/token`, {
    method: "POST",
    body: new URLSearchParams({
      grant_type: "password", client_id: "admin-cli",
      username: env("KEYCLOAK_ADMIN_USERNAME"), password: env("KEYCLOAK_ADMIN_PASSWORD"),
    }),
  });
  if (!response.ok) throw new Error(`admin token: ${response.status}`);
  return (await response.json()).access_token;
}

async function admin(adminUrl, method, path, body) {
  const response = await fetch(`${adminUrl}/admin/realms${path}`, {
    method,
    headers: { authorization: `Bearer ${await adminToken(adminUrl)}`, "content-type": "application/json" },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response;
}

async function authorize(realm, clientId) {
  const verifier = randomBytes(32).toString("base64url");
  const challenge = createHash("sha256").update(verifier).digest("base64url");
  const url = new URL(`${IDENTITY}/realms/${realm}/protocol/openid-connect/auth`);
  url.search = new URLSearchParams({
    client_id: clientId, response_type: "code", scope: "openid", redirect_uri: REDIRECT,
    code_challenge: challenge, code_challenge_method: "S256", state: randomUUID(),
  }).toString();
  return { url: url.toString(), verifier };
}

async function exchange(realm, clientId, callbackUrl, verifier) {
  const code = new URL(callbackUrl).searchParams.get("code");
  const response = await fetch(`${IDENTITY}/realms/${realm}/protocol/openid-connect/token`, {
    method: "POST",
    body: new URLSearchParams({ grant_type: "authorization_code", client_id: clientId, code, redirect_uri: REDIRECT, code_verifier: verifier }),
  });
  return response.ok ? response.json() : null;
}

if (phase === "setup") {
  const adminUrl = env("ADMIN_URL_1");
  const realm = `ha-drill-${Date.now()}`;
  const clientId = "ha-drill-app";
  const user = { email: `ha-${randomUUID()}@example.test`, password: `Ha-${randomUUID()}` };

  await admin(adminUrl, "POST", "", { realm, enabled: true, sslRequired: "external" });
  await admin(adminUrl, "POST", `/${realm}/clients`, {
    clientId, publicClient: true, standardFlowEnabled: true, redirectUris: [REDIRECT],
    attributes: { "pkce.code.challenge.method": "S256" },
  });
  await admin(adminUrl, "POST", `/${realm}/users`, {
    username: user.email, email: user.email, emailVerified: true, enabled: true, firstName: "HA", lastName: "Drill",
    credentials: [{ type: "password", value: user.password, temporary: false }],
  });

  const { url, verifier } = await authorize(realm, clientId);
  const loginPage = await follow(url);
  const html = await loginPage.response.text();
  const action = html.match(/<form[^>]*id="kc-form-login"[^>]*action="([^"]+)"/)?.[1]?.replaceAll("&amp;", "&");
  check("hosted sign-in page served over TLS through Caddy and the edge", Boolean(action));
  const callback = await follow(new URL(action, loginPage.url).toString(), {
    method: "POST", body: new URLSearchParams({ username: user.email, password: user.password }),
  });
  const tokens = await exchange(realm, clientId, callback.url, verifier);
  check("user signs in (authorization code + PKCE)", Boolean(tokens?.refresh_token));

  writeFileSync(stateFile, JSON.stringify({ realm, clientId, refreshToken: tokens.refresh_token, cookies: [...cookies] }));
} else if (phase === "verify") {
  const state = JSON.parse(readFileSync(stateFile, "utf8"));
  for (const [name, value] of state.cookies) cookies.set(name, value);

  // One attempt, immediately after the stop, like a real client: the edge must route around the stopped node itself
  // (it retries connection failures on the other node before its health check notices).
  const refreshed = await fetch(`${IDENTITY}/realms/${state.realm}/protocol/openid-connect/token`, {
    method: "POST",
    body: new URLSearchParams({ grant_type: "refresh_token", client_id: state.clientId, refresh_token: state.refreshToken }),
  });
  check("refresh token still works with node 1 stopped", refreshed.ok, `HTTP ${refreshed.status}`);

  const { url, verifier } = await authorize(state.realm, state.clientId);
  const silent = await follow(url);
  check("browser SSO session survives: silent sign-in without a password", silent.url.startsWith(REDIRECT) && silent.url.includes("code="));
  const tokens = silent.url.startsWith(REDIRECT) ? await exchange(state.realm, state.clientId, silent.url, verifier) : null;
  check("new tokens issued by the remaining node", Boolean(tokens?.access_token));
} else if (phase === "cleanup") {
  const state = JSON.parse(readFileSync(stateFile, "utf8"));
  await admin(env("ADMIN_URL_2"), "DELETE", `/${state.realm}`);
  console.log(`Deleted realm ${state.realm}`);
} else {
  console.error("usage: ha-failover.mjs setup|verify|cleanup");
  process.exitCode = 2;
}
