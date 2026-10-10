import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { createBrowserClient, createServerClient, TokenRequestError } from "../src/index.js";
import { createTokenVerifier, TokenVerificationError } from "../src/node/index.js";
import { API_AUDIENCE, createLiveRealm, REDIRECT_URI, signInThroughHostedPage, type LiveRealm } from "./live-realm.js";

// Contract tests: the SDK against the supported Keycloak version (milestones M5.1, M5.3, M5.6).
describe("SDK against a live Keycloak realm", () => {
  let realm: LiveRealm;

  beforeAll(async () => {
    realm = await createLiveRealm();
  }, 60_000);

  afterAll(async () => {
    await realm?.dispose();
  });

  it("completes Authorization Code + PKCE, refreshes with rotation and verifies the access token", async () => {
    const client = createBrowserClient({ issuer: realm.issuer, clientId: realm.publicClientId, redirectUri: REDIRECT_URI });
    const { url, transaction } = await client.createAuthorizationRequest();

    const callback = await signInThroughHostedPage(url, realm.user.email, realm.user.password);
    const { tokens, user } = await client.completeAuthorization(callback, transaction);

    expect(user.email).toBe(realm.user.email);
    expect(tokens.refreshToken).toBeTruthy();

    const verifier = createTokenVerifier({ issuer: realm.issuer, audience: API_AUDIENCE });
    const verified = await verifier.verify(tokens.accessToken);
    expect(verified.subject).toBe(user.sub);
    expect(verified.clientId).toBe(realm.publicClientId);

    // Refresh tokens rotate: the old one is rejected after use (reuse detection).
    const refreshed = await client.refresh(tokens.refreshToken!);
    expect(refreshed.refreshToken).not.toBe(tokens.refreshToken);
    const reuse = await client.refresh(tokens.refreshToken!).catch((e: unknown) => e);
    expect(reuse).toBeInstanceOf(TokenRequestError);
    expect((reuse as TokenRequestError).requiresSignIn).toBe(true);

    const logout = await client.endSessionUrl({ idToken: refreshed.idToken, postLogoutRedirectUri: "http://localhost:3999/" });
    expect(logout?.pathname).toMatch(/\/logout$/);
  });

  it("issues machine tokens with client credentials and verifies them", async () => {
    const machine = createServerClient({
      issuer: realm.issuer,
      clientId: realm.machineClientId,
      clientSecret: realm.machineSecret,
      redirectUri: REDIRECT_URI,
    });
    const tokens = await machine.clientCredentials();
    expect(tokens.refreshToken).toBeUndefined();

    const verified = await createTokenVerifier({ issuer: realm.issuer, audience: API_AUDIENCE }).verify(tokens.accessToken);
    expect(verified.clientId).toBe(realm.machineClientId);
  });

  it("rejects tokens for another audience or issuer", async () => {
    const machine = createServerClient({ issuer: realm.issuer, clientId: realm.machineClientId, clientSecret: realm.machineSecret, redirectUri: REDIRECT_URI });
    const { accessToken } = await machine.clientCredentials();

    const otherApi = createTokenVerifier({ issuer: realm.issuer, audience: "billing-api" });
    await expect(otherApi.verify(accessToken)).rejects.toMatchObject({ reason: "audience" });

    const otherTenant = createTokenVerifier({ issuer: realm.issuer.replace(realm.name, "dovepeak-platform"), audience: API_AUDIENCE });
    await expect(otherTenant.verify(accessToken)).rejects.toBeInstanceOf(TokenVerificationError);
  });

  it("rejects a revoked session immediately in introspection mode", async () => {
    const client = createBrowserClient({ issuer: realm.issuer, clientId: realm.publicClientId, redirectUri: REDIRECT_URI });
    const { url, transaction } = await client.createAuthorizationRequest();
    const { tokens } = await client.completeAuthorization(await signInThroughHostedPage(url, realm.user.email, realm.user.password), transaction);

    const verifier = createTokenVerifier({
      issuer: realm.issuer,
      audience: API_AUDIENCE,
      introspection: { clientId: realm.machineClientId, clientSecret: realm.machineSecret },
    });
    await expect(verifier.verify(tokens.accessToken)).resolves.toBeDefined();

    const server = createServerClient({ issuer: realm.issuer, clientId: realm.publicClientId, redirectUri: REDIRECT_URI });
    await server.revokeSession(tokens.refreshToken!);
    await expect(verifier.verify(tokens.accessToken)).rejects.toMatchObject({ reason: "inactive" });
  });
});
