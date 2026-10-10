import * as oauth from "oauth4webapi";
import { isBrowser, resolveConfig, type BrowserClientConfig, type ResolvedConfig, type ServerClientConfig } from "./config.js";
import { AuthorizationError, ConfigurationError, DovepeakError, NetworkError, TokenRequestError } from "./errors.js";

/** Tokens from the token endpoint. `expiresAt` values are epoch milliseconds. */
export interface TokenSet {
  accessToken: string;
  expiresAt: number;
  refreshToken?: string;
  refreshExpiresAt?: number;
  idToken?: string;
  scope?: string;
}

/** Validated ID token claims of the signed-in user. */
export interface UserClaims {
  sub: string;
  email?: string;
  emailVerified?: boolean;
  name?: string;
  [claim: string]: unknown;
}

/**
 * State of one sign-in attempt: keep it server-side (or in sessionStorage for a single-page app) between
 * {@link DovepeakClient.createAuthorizationRequest} and {@link DovepeakClient.completeAuthorization}. Each one can
 * complete only once.
 */
export interface AuthorizationTransaction {
  state: string;
  nonce: string;
  codeVerifier: string;
  /** Where to send the user after sign-in (an application path, chosen by the application). */
  returnTo?: string;
}

export interface AuthorizationRequestOptions {
  /** Open the account creation page instead of sign-in. */
  register?: boolean;
  /** Extra scopes for this request (e.g. API scopes granted to the application). */
  scopes?: string[];
  /** Pre-fill the email field. */
  loginHint?: string;
  /** `login` forces re-authentication; `none` checks for an existing session without UI. */
  prompt?: "login" | "none";
  returnTo?: string;
}

/** An OpenID Connect client for one application in one environment. */
export interface DovepeakClient {
  /** OpenID Connect discovery, cached for the client's lifetime. */
  discover(): Promise<oauth.AuthorizationServer>;
  /** Starts Authorization Code + PKCE (S256). Redirect the browser to `url`; keep `transaction` safe. */
  createAuthorizationRequest(options?: AuthorizationRequestOptions): Promise<{ url: URL; transaction: AuthorizationTransaction }>;
  /** Validates the callback (state, issuer), exchanges the code with the PKCE verifier and validates the ID token and nonce. */
  completeAuthorization(callbackUrl: string | URL, transaction: AuthorizationTransaction): Promise<{ tokens: TokenSet; user: UserClaims }>;
  /** Exchanges a refresh token. The identity provider rotates it: always store the new one. */
  refresh(refreshToken: string): Promise<TokenSet>;
  /** RP-initiated logout URL that also ends the identity provider's browser session. */
  endSessionUrl(options: { idToken?: string; postLogoutRedirectUri?: string | URL }): Promise<URL | undefined>;
}

/** Server-side client: adds the client credentials grant and server-to-server session revocation. */
export interface DovepeakServerClient extends DovepeakClient {
  /** Client credentials grant for machine-to-machine applications. Requires a client secret. */
  clientCredentials(options?: { scopes?: string[] }): Promise<TokenSet>;
  /** Ends the identity provider session that owns the refresh token, server to server. Never throws. */
  revokeSession(refreshToken: string): Promise<void>;
}

/**
 * A client for browser code (public clients: single-page and native apps). Refuses a client secret, because
 * everything in a browser bundle is public. Prefer a backend-for-frontend (`@dovepeak/identity/next`) for web apps.
 */
export function createBrowserClient(config: BrowserClientConfig): DovepeakClient {
  if ((config as { clientSecret?: unknown }).clientSecret !== undefined) {
    throw new ConfigurationError("A client secret must never be used in browser code. Use createServerClient on your server instead.");
  }

  return buildClient(resolveConfig(config));
}

/** A client for server-side code. Throws when loaded in a browser, so a secret cannot leak into a bundle unnoticed. */
export function createServerClient(config: ServerClientConfig): DovepeakServerClient {
  if (isBrowser()) {
    throw new ConfigurationError("createServerClient must only be used on the server. Use createBrowserClient in browser code.");
  }

  return buildClient(resolveConfig(config));
}

function buildClient(config: ResolvedConfig): DovepeakServerClient {
  const client: oauth.Client = { client_id: config.clientId };
  const clientAuth = config.clientSecret ? oauth.ClientSecretPost(config.clientSecret) : oauth.None();
  const options = {
    ...(config.insecure ? { [oauth.allowInsecureRequests]: true } : {}),
    ...(config.fetch ? { [oauth.customFetch]: config.fetch } : {}),
  };

  let discovery: Promise<oauth.AuthorizationServer> | undefined;
  const discover = () => {
    discovery ??= guard(async () => {
      const response = await oauth.discoveryRequest(config.issuer, options);
      return oauth.processDiscoveryResponse(config.issuer, response);
    }).catch((error: unknown) => {
      discovery = undefined;
      throw error;
    });
    return discovery;
  };

  const toTokenSet = (result: oauth.TokenEndpointResponse, previousIdToken?: string): TokenSet => {
    const now = Date.now();
    const refreshExpiresIn = (result as Record<string, unknown>)["refresh_expires_in"];
    return {
      accessToken: result.access_token,
      expiresAt: now + (result.expires_in ?? 300) * 1000,
      ...(result.refresh_token ? { refreshToken: result.refresh_token } : {}),
      ...(typeof refreshExpiresIn === "number" && refreshExpiresIn > 0 ? { refreshExpiresAt: now + refreshExpiresIn * 1000 } : {}),
      ...((result.id_token ?? previousIdToken) ? { idToken: result.id_token ?? previousIdToken } : {}),
      ...(result.scope ? { scope: result.scope } : {}),
    };
  };

  return {
    discover,

    async createAuthorizationRequest(request = {}) {
      const as = await discover();
      const transaction: AuthorizationTransaction = {
        state: oauth.generateRandomState(),
        nonce: oauth.generateRandomNonce(),
        codeVerifier: oauth.generateRandomCodeVerifier(),
        ...(request.returnTo ? { returnTo: request.returnTo } : {}),
      };

      const endpoint = request.register
        ? as.authorization_endpoint!.replace(/\/auth$/, "/registrations")
        : as.authorization_endpoint!;
      const url = new URL(endpoint);
      url.searchParams.set("client_id", config.clientId);
      url.searchParams.set("response_type", "code");
      url.searchParams.set("redirect_uri", config.redirectUri.toString());
      url.searchParams.set("scope", [...new Set([...config.scopes, ...(request.scopes ?? [])])].join(" "));
      url.searchParams.set("state", transaction.state);
      url.searchParams.set("nonce", transaction.nonce);
      url.searchParams.set("code_challenge", await oauth.calculatePKCECodeChallenge(transaction.codeVerifier));
      url.searchParams.set("code_challenge_method", "S256");
      if (request.loginHint) url.searchParams.set("login_hint", request.loginHint);
      if (request.prompt) url.searchParams.set("prompt", request.prompt);
      return { url, transaction };
    },

    async completeAuthorization(callbackUrl, transaction) {
      const as = await discover();
      let params: URLSearchParams;
      try {
        params = oauth.validateAuthResponse(as, client, new URL(callbackUrl.toString()), transaction.state);
      } catch (error) {
        if (error instanceof oauth.AuthorizationResponseError) {
          throw new AuthorizationError(`Sign-in did not complete: ${error.error}.`, error.error, { cause: error });
        }

        throw new AuthorizationError("The sign-in callback is invalid (state or issuer mismatch).", undefined, { cause: error });
      }

      return guard(async () => {
        const response = await oauth.authorizationCodeGrantRequest(
          as, client, clientAuth, params, config.redirectUri.toString(), transaction.codeVerifier, options,
        );
        const result = await oauth.processAuthorizationCodeResponse(as, client, response, {
          expectedNonce: transaction.nonce,
          requireIdToken: true,
        });
        const claims = oauth.getValidatedIdTokenClaims(result)!;
        const user: UserClaims = {
          ...claims,
          sub: claims.sub,
          ...(typeof claims["email"] === "string" ? { email: claims["email"] } : {}),
          ...(typeof claims["email_verified"] === "boolean" ? { emailVerified: claims["email_verified"] } : {}),
          ...(typeof claims["name"] === "string" ? { name: claims["name"] } : {}),
        };
        return { tokens: toTokenSet(result), user };
      });
    },

    async refresh(refreshToken) {
      const as = await discover();
      return guard(async () => {
        const response = await oauth.refreshTokenGrantRequest(as, client, clientAuth, refreshToken, options);
        return toTokenSet(await oauth.processRefreshTokenResponse(as, client, response));
      });
    },

    async endSessionUrl({ idToken, postLogoutRedirectUri }) {
      const as = await discover();
      if (!as.end_session_endpoint) return undefined;
      const url = new URL(as.end_session_endpoint);
      url.searchParams.set("client_id", config.clientId);
      if (postLogoutRedirectUri) url.searchParams.set("post_logout_redirect_uri", postLogoutRedirectUri.toString());
      if (idToken) url.searchParams.set("id_token_hint", idToken);
      return url;
    },

    async clientCredentials(request = {}) {
      if (!config.clientSecret) {
        throw new ConfigurationError("The client credentials grant requires a client secret (machine-to-machine application).");
      }

      const as = await discover();
      return guard(async () => {
        const parameters = new URLSearchParams();
        if (request.scopes && request.scopes.length > 0) parameters.set("scope", request.scopes.join(" "));
        const response = await oauth.clientCredentialsGrantRequest(as, client, clientAuth, parameters, options);
        return toTokenSet(await oauth.processClientCredentialsResponse(as, client, response));
      });
    },

    async revokeSession(refreshToken) {
      try {
        const as = await discover();
        if (!as.end_session_endpoint) return;
        const body = new URLSearchParams({ client_id: config.clientId, refresh_token: refreshToken });
        if (config.clientSecret) body.set("client_secret", config.clientSecret);
        await (config.fetch ?? fetch)(as.end_session_endpoint, { method: "POST", body });
      } catch {
        // Best effort: the local session is ended regardless.
      }
    },
  };
}

/** Maps oauth4webapi and network failures to the SDK's typed errors. */
async function guard<T>(work: () => Promise<T>): Promise<T> {
  try {
    return await work();
  } catch (error) {
    if (error instanceof DovepeakError) throw error;
    if (error instanceof oauth.ResponseBodyError) {
      throw new TokenRequestError(error.error, error.error_description, { cause: error });
    }
    if (error instanceof oauth.AuthorizationResponseError) {
      throw new AuthorizationError(`Sign-in did not complete: ${error.error}.`, error.error, { cause: error });
    }
    if (error instanceof TypeError) {
      throw new NetworkError("The identity provider could not be reached.", { cause: error });
    }
    if (error instanceof oauth.OperationProcessingError) {
      throw new DovepeakError("protocol_error", `The identity provider's response was rejected: ${error.message}`, { cause: error });
    }
    throw new DovepeakError("unexpected_error", error instanceof Error ? error.message : "Unexpected error.", { cause: error });
  }
}
