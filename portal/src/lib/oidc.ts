import "server-only";
import * as oauth from "oauth4webapi";
import { config, redirectUri } from "./config";

// The portal is a public client (no secret) of the platform realm: Authorization Code + PKCE, with tokens held on
// the server. Plain HTTP is only tolerated for a loopback issuer during local development.

/**
 * Server-to-identity-engine calls. In containers the public issuer origin (e.g. localhost:8080) is not reachable,
 * so requests to it are sent to IDENTITY_INTERNAL_URL instead. Keycloak answers backchannel requests with internal
 * endpoint URLs while keeping the public issuer, so token validation is unaffected.
 */
const customFetch: typeof fetch = (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input.toString());
  if (config.identityInternalUrl && url.origin === config.issuer.origin) {
    url.protocol = config.identityInternalUrl.protocol;
    url.host = config.identityInternalUrl.host;
  }

  return fetch(url, init);
};

function requestOptions() {
  const issuer = config.issuer;
  const insecureLocalIssuer = issuer.protocol === "http:" && ["localhost", "127.0.0.1"].includes(issuer.hostname);
  return {
    [oauth.customFetch]: customFetch,
    ...(insecureLocalIssuer || config.identityInternalUrl?.protocol === "http:" ? { [oauth.allowInsecureRequests]: true } : {}),
  };
}

const client = (): oauth.Client => ({ client_id: config.clientId });
const clientAuth = oauth.None();

let discovery: Promise<oauth.AuthorizationServer> | undefined;

/** OpenID Connect discovery, cached for the process lifetime. */
export function authorizationServer(): Promise<oauth.AuthorizationServer> {
  discovery ??= oauth
    .discoveryRequest(config.issuer, requestOptions())
    .then((response) => oauth.processDiscoveryResponse(config.issuer, response))
    .catch((error: unknown) => {
      discovery = undefined;
      throw error;
    });
  return discovery;
}

/** Browser-facing endpoints must use the public origin even when discovery was fetched internally. */
function publicEndpoint(endpoint: string): URL {
  const url = new URL(endpoint);
  if (config.identityInternalUrl && url.origin === config.identityInternalUrl.origin) {
    url.protocol = config.issuer.protocol;
    url.host = config.issuer.host;
  }

  return url;
}

export interface PendingAuthorization {
  codeVerifier: string;
  state: string;
  nonce: string;
  returnTo: string;
}

export async function createAuthorizationUrl(options: { register: boolean; returnTo: string }) {
  const as = await authorizationServer();
  const pending: PendingAuthorization = {
    codeVerifier: oauth.generateRandomCodeVerifier(),
    state: oauth.generateRandomState(),
    nonce: oauth.generateRandomNonce(),
    returnTo: options.returnTo,
  };

  const endpoint = options.register
    ? as.authorization_endpoint!.replace(/\/auth$/, "/registrations")
    : as.authorization_endpoint!;

  const url = publicEndpoint(endpoint);
  url.searchParams.set("client_id", config.clientId);
  url.searchParams.set("response_type", "code");
  url.searchParams.set("scope", "openid email profile");
  url.searchParams.set("redirect_uri", redirectUri());
  url.searchParams.set("state", pending.state);
  url.searchParams.set("nonce", pending.nonce);
  url.searchParams.set("code_challenge", await oauth.calculatePKCECodeChallenge(pending.codeVerifier));
  url.searchParams.set("code_challenge_method", "S256");

  return { url, pending };
}

export interface TokenSet {
  accessToken: string;
  accessTokenExpiresAt: number;
  refreshToken: string;
  refreshTokenExpiresInSeconds: number;
  idToken: string;
}

function toTokenSet(result: oauth.TokenEndpointResponse, previousIdToken?: string): TokenSet {
  const refreshExpiresIn = Number((result as Record<string, unknown>)["refresh_expires_in"] ?? 1800);
  return {
    accessToken: result.access_token,
    accessTokenExpiresAt: Date.now() + (result.expires_in ?? 300) * 1000,
    refreshToken: result.refresh_token!,
    refreshTokenExpiresInSeconds: refreshExpiresIn,
    idToken: result.id_token ?? previousIdToken ?? "",
  };
}

/** Validates the callback (state, iss), exchanges the code with PKCE and validates the ID token (incl. nonce). */
export async function completeAuthorization(callbackUrl: URL, pending: PendingAuthorization) {
  const as = await authorizationServer();
  const params = oauth.validateAuthResponse(as, client(), callbackUrl, pending.state);
  const response = await oauth.authorizationCodeGrantRequest(
    as, client(), clientAuth, params, redirectUri(), pending.codeVerifier, requestOptions(),
  );
  const result = await oauth.processAuthorizationCodeResponse(as, client(), response, {
    expectedNonce: pending.nonce,
    requireIdToken: true,
  });

  const claims = oauth.getValidatedIdTokenClaims(result)!;
  return {
    tokens: toTokenSet(result),
    user: {
      sub: claims.sub,
      email: typeof claims["email"] === "string" ? claims["email"] : undefined,
      name: typeof claims["name"] === "string" ? claims["name"] : undefined,
    },
  };
}

/** Refresh grant. The identity provider rotates the refresh token on every call. */
export async function refreshTokens(refreshToken: string, previousIdToken: string): Promise<TokenSet> {
  const as = await authorizationServer();
  const response = await oauth.refreshTokenGrantRequest(as, client(), clientAuth, refreshToken, requestOptions());
  const result = await oauth.processRefreshTokenResponse(as, client(), response);
  return toTokenSet(result, previousIdToken);
}

/** Server-to-server revocation of the identity provider session that owns the refresh token. */
export async function revokeSession(refreshToken: string): Promise<void> {
  const as = await authorizationServer();
  if (!as.end_session_endpoint) return;

  const body = new URLSearchParams({ client_id: config.clientId, refresh_token: refreshToken });
  await customFetch(as.end_session_endpoint, { method: "POST", body }).catch(() => undefined);
}

/** RP-initiated logout URL: ends the identity provider's browser session too. */
export async function endSessionUrl(idToken: string): Promise<URL | undefined> {
  const as = await authorizationServer();
  if (!as.end_session_endpoint) return undefined;

  const url = publicEndpoint(as.end_session_endpoint);
  url.searchParams.set("client_id", config.clientId);
  url.searchParams.set("post_logout_redirect_uri", new URL("/", config.appUrl).toString());
  if (idToken) url.searchParams.set("id_token_hint", idToken);
  return url;
}
