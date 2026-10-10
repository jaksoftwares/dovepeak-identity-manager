import { afterEach, describe, expect, it } from "vitest";
import {
  AuthorizationError,
  ConfigurationError,
  createBrowserClient,
  createServerClient,
  NetworkError,
  TokenRequestError,
} from "../src/index.js";
import { createFakeIssuer } from "./fake-issuer.js";

const redirectUri = "https://app.example.test/callback";

describe("configuration", () => {
  afterEach(() => {
    delete (globalThis as Record<string, unknown>)["window"];
    delete (globalThis as Record<string, unknown>)["document"];
  });

  it("refuses a client secret in browser code", () => {
    expect(() =>
      createBrowserClient({ issuer: "https://id.example.test/realms/x", clientId: "a", redirectUri, clientSecret: "s" } as never),
    ).toThrow(ConfigurationError);
  });

  it("refuses to create a server client in a browser", () => {
    Object.assign(globalThis, { window: {}, document: {} });
    expect(() => createServerClient({ issuer: "https://id.example.test/realms/x", clientId: "a", redirectUri })).toThrow(ConfigurationError);
  });

  it("requires HTTPS except for a localhost issuer", () => {
    expect(() => createServerClient({ issuer: "http://id.example.test/realms/x", clientId: "a", redirectUri })).toThrow(ConfigurationError);
    expect(() => createServerClient({ issuer: "http://localhost:8080/realms/x", clientId: "a", redirectUri })).not.toThrow();
  });
});

describe("authorization code flow", () => {
  it("builds a PKCE request and completes it, validating state and nonce", async () => {
    const fake = await createFakeIssuer();
    const client = createServerClient({ issuer: fake.issuer, clientId: fake.clientId, redirectUri, fetch: fake.fetch, scopes: ["openid", "email"] });

    const { url, transaction } = await client.createAuthorizationRequest({ scopes: ["orders:read"], returnTo: "/orders" });
    expect(url.pathname).toMatch(/\/auth$/);
    expect(url.searchParams.get("code_challenge_method")).toBe("S256");
    expect(url.searchParams.get("code_challenge")).toHaveLength(43);
    expect(url.searchParams.get("scope")).toBe("openid email orders:read");
    expect(url.searchParams.get("redirect_uri")).toBe(redirectUri);
    expect(transaction.returnTo).toBe("/orders");

    fake.state.nonce = transaction.nonce;
    const { tokens, user } = await client.completeAuthorization(
      `${redirectUri}?code=abc&state=${transaction.state}&iss=${encodeURIComponent(fake.issuer)}`,
      transaction,
    );

    expect(user).toMatchObject({ sub: "user-1", email: "user@example.test", emailVerified: true });
    expect(tokens.refreshToken).toBe("refresh-1");
    expect(tokens.expiresAt).toBeGreaterThan(Date.now());
    const exchange = fake.state.requests.find((r) => r.body.get("grant_type") === "authorization_code")!;
    expect(exchange.body.get("code_verifier")).toBe(transaction.codeVerifier);
  });

  it("uses the registration endpoint when asked", async () => {
    const fake = await createFakeIssuer();
    const client = createBrowserClient({ issuer: fake.issuer, clientId: fake.clientId, redirectUri, fetch: fake.fetch });
    const { url } = await client.createAuthorizationRequest({ register: true });
    expect(url.pathname).toMatch(/\/registrations$/);
  });

  it("rejects a callback with the wrong state", async () => {
    const fake = await createFakeIssuer();
    const client = createServerClient({ issuer: fake.issuer, clientId: fake.clientId, redirectUri, fetch: fake.fetch });
    const { transaction } = await client.createAuthorizationRequest();

    await expect(client.completeAuthorization(`${redirectUri}?code=abc&state=forged&iss=${encodeURIComponent(fake.issuer)}`, transaction))
      .rejects.toBeInstanceOf(AuthorizationError);
  });

  it("surfaces an error returned to the callback", async () => {
    const fake = await createFakeIssuer();
    const client = createServerClient({ issuer: fake.issuer, clientId: fake.clientId, redirectUri, fetch: fake.fetch });
    const { transaction } = await client.createAuthorizationRequest();

    const error = await client
      .completeAuthorization(`${redirectUri}?error=access_denied&state=${transaction.state}&iss=${encodeURIComponent(fake.issuer)}`, transaction)
      .catch((e: unknown) => e);
    expect(error).toBeInstanceOf(AuthorizationError);
    expect((error as AuthorizationError).error).toBe("access_denied");
  });

  it("rejects an ID token with the wrong nonce", async () => {
    const fake = await createFakeIssuer();
    const client = createServerClient({ issuer: fake.issuer, clientId: fake.clientId, redirectUri, fetch: fake.fetch });
    const { transaction } = await client.createAuthorizationRequest();
    fake.state.nonce = "someone-elses-nonce";

    await expect(client.completeAuthorization(`${redirectUri}?code=abc&state=${transaction.state}&iss=${encodeURIComponent(fake.issuer)}`, transaction))
      .rejects.toMatchObject({ code: "protocol_error" });
  });
});

describe("token requests", () => {
  it("reports a used or revoked refresh token as requiring sign-in", async () => {
    const fake = await createFakeIssuer();
    const client = createServerClient({ issuer: fake.issuer, clientId: fake.clientId, redirectUri, fetch: fake.fetch });
    fake.state.tokenResponses.push(async () => fake.json({ error: "invalid_grant", error_description: "Token is not active" }, 400));

    const error = await client.refresh("old").catch((e: unknown) => e);
    expect(error).toBeInstanceOf(TokenRequestError);
    expect((error as TokenRequestError).requiresSignIn).toBe(true);
    expect((error as Error).message).not.toContain("old");
  });

  it("requires a secret for client credentials, and sends requested scopes", async () => {
    const fake = await createFakeIssuer();
    const publicClient = createServerClient({ issuer: fake.issuer, clientId: fake.clientId, redirectUri, fetch: fake.fetch });
    await expect(publicClient.clientCredentials()).rejects.toBeInstanceOf(ConfigurationError);

    const machine = createServerClient({ issuer: fake.issuer, clientId: fake.clientId, clientSecret: "s3cret", redirectUri, fetch: fake.fetch });
    const tokens = await machine.clientCredentials({ scopes: ["orders:read"] });
    expect(tokens.refreshToken).toBeUndefined();
    const request = fake.state.requests.find((r) => r.body.get("grant_type") === "client_credentials")!;
    expect(request.body.get("scope")).toBe("orders:read");
  });

  it("maps an unreachable identity provider to NetworkError", async () => {
    const client = createServerClient({
      issuer: "https://id.example.test/realms/x",
      clientId: "a",
      redirectUri,
      fetch: async () => {
        throw new TypeError("fetch failed");
      },
    });

    await expect(client.discover()).rejects.toBeInstanceOf(NetworkError);
  });
});
