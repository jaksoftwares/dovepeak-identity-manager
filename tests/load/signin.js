// k6 load test: hosted sign-in and token refresh through the public edge (milestones M6.1 SLO, M6.4).
//
// Each iteration is a complete browser sign-in (authorization code + PKCE) followed by a token refresh, against a
// throwaway realm with its own users. Thresholds encode the sign-in latency objective: p95 < 300 ms.
// Run with tests/load/run.sh, which supplies the environment below and lifts the per-IP edge limits for the run.
//
// IDENTITY_URL  public identity URL (through Caddy and the edge)
// ADMIN_URL     internal Keycloak URL for setup and teardown (admin API)
// ADMIN_USER, ADMIN_PASSWORD   Keycloak bootstrap administrator
// VUS, DURATION                load shape (default 10 virtual users for 2 minutes)

import http from "k6/http";
import { check, fail, sleep } from "k6";
import { parseHTML } from "k6/html";
import crypto from "k6/crypto";
import encoding from "k6/encoding";

const IDENTITY = __ENV.IDENTITY_URL;
const ADMIN = __ENV.ADMIN_URL;
const REDIRECT = "https://app.example.test/callback";
const USERS = 50;
const PASSWORD = "Load-test-password-1";

export const options = {
  scenarios: {
    signin: {
      executor: "ramping-vus",
      startVUs: 1,
      stages: [
        { duration: "30s", target: Number(__ENV.VUS ?? 10) },
        { duration: __ENV.DURATION ?? "2m", target: Number(__ENV.VUS ?? 10) },
        { duration: "15s", target: 0 },
      ],
    },
  },
  thresholds: {
    "http_req_duration{step:login}": ["p(95)<300"],
    "http_req_duration{step:token}": ["p(95)<300"],
    "http_req_duration{step:refresh}": ["p(95)<300"],
    "http_req_duration{step:authorize}": ["p(95)<300"],
    http_req_failed: ["rate<0.01"],
    checks: ["rate>0.99"],
  },
  setupTimeout: "5m",
  teardownTimeout: "2m",
};

function adminHeaders() {
  const response = http.post(`${ADMIN}/realms/master/protocol/openid-connect/token`, {
    grant_type: "password", client_id: "admin-cli", username: __ENV.ADMIN_USER, password: __ENV.ADMIN_PASSWORD,
  });
  if (response.status !== 200) fail(`admin token: ${response.status}`);
  return { Authorization: `Bearer ${response.json("access_token")}`, "Content-Type": "application/json" };
}

export function setup() {
  const realm = `load-${Date.now()}`;
  const headers = adminHeaders();
  const create = (path, body) => {
    const response = http.post(`${ADMIN}/admin/realms${path}`, JSON.stringify(body), { headers });
    if (response.status !== 201) fail(`POST ${path}: ${response.status} ${response.body}`);
  };

  create("", { realm, enabled: true, sslRequired: "external" });
  create(`/${realm}/clients`, {
    clientId: "load-app", publicClient: true, standardFlowEnabled: true, redirectUris: [REDIRECT],
    attributes: { "pkce.code.challenge.method": "S256" },
  });
  for (let i = 0; i < USERS; i++) {
    create(`/${realm}/users`, {
      username: `user${i}@example.test`, email: `user${i}@example.test`, emailVerified: true, enabled: true,
      firstName: "Load", lastName: `User ${i}`,
      credentials: [{ type: "password", value: PASSWORD, temporary: false }],
    });
  }

  return { realm };
}

export default function ({ realm }) {
  const base = `${IDENTITY}/realms/${realm}/protocol/openid-connect`;
  const user = `user${(__VU - 1) % USERS}@example.test`;
  const verifier = encoding.b64encode(crypto.randomBytes(32), "rawurl");
  const challenge = crypto.sha256(verifier, "base64rawurl");
  const jar = http.cookieJar();
  jar.clear(IDENTITY);

  const authorize = http.get(
    `${base}/auth?client_id=load-app&response_type=code&scope=openid&redirect_uri=${encodeURIComponent(REDIRECT)}` +
      `&code_challenge=${challenge}&code_challenge_method=S256&state=${__VU}-${__ITER}`,
    { tags: { step: "authorize" } },
  );
  const action = parseHTML(authorize.body).find("#kc-form-login").attr("action");
  if (!check(authorize, { "login page served": () => Boolean(action) })) return;

  const login = http.post(action, { username: user, password: PASSWORD }, { redirects: 0, tags: { step: "login" } });
  const location = login.headers.Location ?? "";
  const code = (location.match(/[?&]code=([^&]+)/) ?? [])[1];
  if (!check(login, { "signed in": () => login.status === 302 && Boolean(code) })) return;

  const token = http.post(`${base}/token`, {
    grant_type: "authorization_code", client_id: "load-app", code, redirect_uri: REDIRECT, code_verifier: verifier,
  }, { tags: { step: "token" } });
  if (!check(token, { "tokens issued": (r) => r.status === 200 })) return;

  const refresh = http.post(`${base}/token`, {
    grant_type: "refresh_token", client_id: "load-app", refresh_token: token.json("refresh_token"),
  }, { tags: { step: "refresh" } });
  check(refresh, { "token refreshed": (r) => r.status === 200 });

  sleep(1);
}

export function teardown({ realm }) {
  http.del(`${ADMIN}/admin/realms/${realm}`, null, { headers: adminHeaders() });
}
