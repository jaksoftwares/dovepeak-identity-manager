#!/usr/bin/env node
// Email provider outage drill (runbook docs/runbooks/email-provider-outage.md, milestone M6.3).
//
// With the mail relay pointed at an unreachable provider, requests password resets for an existing account through the
// public sign-in pages and reports what the user sees. Run inside a container on the frontend network (see the runbook).
//
// Environment: IDENTITY_URL, PORTAL_URL, REALM (default dovepeak-platform), CLIENT_ID (default dovepeak-portal),
// EMAIL (an existing account), ATTEMPTS (default 3).

import { createHash, randomBytes } from "node:crypto";

const IDENTITY = process.env.IDENTITY_URL;
const REALM = process.env.REALM ?? "dovepeak-platform";
const CLIENT_ID = process.env.CLIENT_ID ?? "dovepeak-portal";
const REDIRECT = `${process.env.PORTAL_URL}/api/auth/callback`;
const EMAIL = process.env.EMAIL;
const ATTEMPTS = Number(process.env.ATTEMPTS ?? 3);

async function resetOnce() {
  const cookies = new Map();
  const request = async (url, init = {}) => {
    const headers = new Headers(init.headers);
    if (cookies.size) headers.set("cookie", [...cookies].map(([k, v]) => `${k}=${v}`).join("; "));
    const response = await fetch(url, { ...init, headers, redirect: "manual" });
    for (const header of response.headers.getSetCookie()) {
      const [pair] = header.split(";");
      const index = pair.indexOf("=");
      cookies.set(pair.slice(0, index).trim(), pair.slice(index + 1).trim());
    }
    return response;
  };

  const verifier = randomBytes(32).toString("base64url");
  const auth = new URL(`${IDENTITY}/realms/${REALM}/protocol/openid-connect/auth`);
  auth.search = new URLSearchParams({
    client_id: CLIENT_ID, response_type: "code", scope: "openid", redirect_uri: REDIRECT, state: "drill",
    code_challenge: createHash("sha256").update(verifier).digest("base64url"), code_challenge_method: "S256",
  }).toString();

  const login = await (await request(auth)).text();
  const resetLink = login.match(/href="([^"]*login-actions\/reset-credentials[^"]*)"/)?.[1]?.replaceAll("&amp;", "&");
  const resetPage = await (await request(new URL(resetLink, IDENTITY))).text();
  const action = resetPage.match(/<form[^>]*id="kc-reset-password-form"[^>]*action="([^"]+)"/)?.[1]?.replaceAll("&amp;", "&");
  const result = await request(action, { method: "POST", body: new URLSearchParams({ username: EMAIL }) });
  const page = await result.text();
  const message = page.match(/(Failed to send email[^<]*|You should receive an email[^<]*)/i)?.[1] ?? `HTTP ${result.status}`;
  return message.trim();
}

for (let i = 1; i <= ATTEMPTS; i++) {
  console.log(`reset ${i}: ${await resetOnce()}`);
}
