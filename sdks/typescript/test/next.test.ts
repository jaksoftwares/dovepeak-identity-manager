import { describe, expect, it } from "vitest";
import { createDovepeakAuth, memorySessionStore } from "../src/next/index.js";
import { createFakeIssuer } from "./fake-issuer.js";

const appUrl = "http://localhost:3000";

async function setup() {
  const fake = await createFakeIssuer();
  const store = memorySessionStore();
  const auth = createDovepeakAuth({
    client: { issuer: fake.issuer, clientId: fake.clientId, clientSecret: "s3cret", fetch: fake.fetch },
    appUrl,
    store,
  });
  return { fake, store, auth };
}

const cookieValue = (response: Response, name: string) =>
  response.headers.getSetCookie().find((c) => c.startsWith(`${name}=`))?.split(";")[0]!.slice(name.length + 1);

/** Signs in through login and callback, returning the session cookie value. */
async function signIn(setupResult: Awaited<ReturnType<typeof setup>>, returnTo?: string) {
  const { auth, fake } = setupResult;
  const login = await auth.handler(new Request(`${appUrl}/api/auth/login${returnTo ? `?returnTo=${encodeURIComponent(returnTo)}` : ""}`));
  const authorize = new URL(login.headers.get("location")!);
  const tx = cookieValue(login, "dp_tx")!;
  fake.state.nonce = authorize.searchParams.get("nonce")!;

  const callback = await auth.handler(new Request(
    `${appUrl}/api/auth/callback?code=abc&state=${authorize.searchParams.get("state")}&iss=${encodeURIComponent(fake.issuer)}`,
    { headers: { cookie: `dp_tx=${tx}` } },
  ));
  return { login, callback, session: cookieValue(callback, "dp_session")! };
}

describe("createDovepeakAuth", () => {
  it("signs in with a server-side session and an HttpOnly cookie", async () => {
    const s = await setup();
    const { login, callback, session } = await signIn(s, "/orders");

    expect(login.status).toBe(307);
    expect(login.headers.get("set-cookie")).toContain("HttpOnly");
    expect(callback.headers.get("location")).toBe(`${appUrl}/orders`);
    expect(session).toBeTruthy();
    expect(callback.headers.getSetCookie().join()).toMatch(/dp_session=[^;]+; Path=\/; HttpOnly; SameSite=Lax/);

    const me = await s.auth.handler(new Request(`${appUrl}/api/auth/session`, { headers: { cookie: `dp_session=${session}` } }));
    const body = await me.json();
    expect(body).toEqual({ authenticated: true, user: { sub: "user-1", email: "user@example.test" } });
    expect(JSON.stringify(body)).not.toContain("eyJ"); // no tokens reach the browser
  });

  it("ignores off-site return paths (open redirect)", async () => {
    const s = await setup();
    const { callback } = await signIn(s, "//evil.example/phish");
    expect(callback.headers.get("location")).toBe(`${appUrl}/`);
  });

  it("completes each sign-in transaction only once", async () => {
    const s = await setup();
    const login = await s.auth.handler(new Request(`${appUrl}/api/auth/login`));
    const tx = cookieValue(login, "dp_tx")!;
    const state = new URL(login.headers.get("location")!).searchParams.get("state");
    const replay = () => s.auth.handler(new Request(`${appUrl}/api/auth/callback?code=x&state=${state}`, { headers: { cookie: `dp_tx=${tx}` } }));

    await replay();
    expect((await replay()).headers.get("location")).toBe(`${appUrl}/?error=expired`);
  });

  it("refuses sign-out without POST from the app's own origin (CSRF)", async () => {
    const s = await setup();
    const { session } = await signIn(s);
    const headers = { cookie: `dp_session=${session}` };

    expect((await s.auth.handler(new Request(`${appUrl}/api/auth/logout`, { headers }))).status).toBe(403);
    expect((await s.auth.handler(new Request(`${appUrl}/api/auth/logout`, { method: "POST", headers: { ...headers, origin: "https://evil.example" } }))).status).toBe(403);

    const out = await s.auth.handler(new Request(`${appUrl}/api/auth/logout`, { method: "POST", headers: { ...headers, origin: appUrl } }));
    expect(out.status).toBe(303);
    expect(out.headers.get("location")).toContain("/protocol/openid-connect/logout");
    const after = await s.auth.handler(new Request(`${appUrl}/api/auth/session`, { headers }));
    expect(await after.json()).toEqual({ authenticated: false });
  });

  it("refreshes an expiring access token once, even for concurrent requests", async () => {
    const s = await setup();
    const { session } = await signIn(s);
    const api = s.auth.withCookies(async () => ({ get: (name: string) => (name === "dp_session" ? { value: session } : undefined) }));

    // Make the stored access token expire.
    const stored = JSON.parse((await s.store.get(`session:${session}`))!);
    stored.tokens.expiresAt = Date.now() - 1000;
    await s.store.set(`session:${session}`, JSON.stringify(stored), 600);

    const tokens = await Promise.all([api.getAccessToken(), api.getAccessToken(), api.getAccessToken()]);
    expect(new Set(tokens).size).toBe(1);
    expect(s.fake.state.requests.filter((r) => r.body.get("grant_type") === "refresh_token")).toHaveLength(1);
  });

  it("ends the session when the identity provider rejects the refresh token", async () => {
    const s = await setup();
    const { session } = await signIn(s);
    const api = s.auth.withCookies(async () => ({ get: () => ({ value: session }) }));
    const stored = JSON.parse((await s.store.get(`session:${session}`))!);
    stored.tokens.expiresAt = Date.now() - 1000;
    await s.store.set(`session:${session}`, JSON.stringify(stored), 600);
    s.fake.state.tokenResponses.push(async () => s.fake.json({ error: "invalid_grant" }, 400));

    expect(await api.getAccessToken()).toBeNull();
    expect(await api.getSession()).toBeNull();
  });

  it("requires HTTPS for the app outside localhost", () => {
    expect(() => createDovepeakAuth({ client: { issuer: "https://id.example.test/realms/x", clientId: "a" }, appUrl: "http://shop.example.com", store: memorySessionStore() }))
      .toThrow(/HTTPS/);
  });
});
