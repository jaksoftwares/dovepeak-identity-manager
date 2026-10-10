import { createServerClient, type AuthorizationTransaction, type DovepeakServerClient, type TokenSet, type UserClaims } from "../client.js";
import type { ServerClientConfig } from "../config.js";
import { ConfigurationError, TokenRequestError } from "../errors.js";
import type { SessionStore } from "./session-store.js";

export interface SessionUser {
  sub: string;
  email?: string;
  name?: string;
}

interface StoredSession {
  user: SessionUser;
  tokens: TokenSet;
}

export interface DovepeakAuthOptions {
  /** The application's settings. `redirectUri` defaults to `{appUrl}{basePath}/callback`. */
  client: Omit<ServerClientConfig, "redirectUri"> & { redirectUri?: string | URL };
  /** Public URL of the application, e.g. https://shop.example.com. Used for redirects and the CSRF origin check. */
  appUrl: string | URL;
  /** Where sessions and sign-in transactions are kept (Redis/Valkey in production). */
  store: SessionStore;
  /** Mount path of the catch-all route handler. Defaults to `/api/auth`. */
  basePath?: string;
  /** Default destination after sign-in. Defaults to `/`. */
  afterSignInPath?: string;
  /** Destination after sign-out. Defaults to `/`. */
  afterSignOutPath?: string;
  /** Refresh the access token this long before it expires. Defaults to 30 seconds. */
  refreshLeewaySeconds?: number;
}

/** Reads cookies of the current request (server components, route handlers). */
export type CookieReader = () => Promise<{ get(name: string): { value: string } | undefined }>;

export interface SessionApi {
  /** The signed-in user, or null. Does not refresh tokens. */
  getSession(): Promise<SessionUser | null>;
  /**
   * A valid access token for calling APIs, refreshed when about to expire, or null when signed out.
   * Only one request per session refreshes at a time: refresh tokens are single-use, and a concurrent second
   * refresh would be treated as token theft and end the session.
   */
  getAccessToken(): Promise<string | null>;
}

export interface DovepeakAuth extends SessionApi {
  /** Route handler for `{basePath}/[...dovepeak]`: login, callback, logout (POST) and session. */
  handler(request: Request): Promise<Response>;
  GET(request: Request): Promise<Response>;
  POST(request: Request): Promise<Response>;
  /** Session helpers bound to an explicit cookie reader (for frameworks other than Next.js, and tests). */
  withCookies(read: CookieReader): SessionApi;
  /** The server client, for example to call `clientCredentials`. */
  client: DovepeakServerClient;
  /** Name of the session cookie. */
  cookieName: string;
  loginPath(returnTo?: string): string;
}

const TRANSACTION_TTL_SECONDS = 600;
const LOCK_MS = 10_000;

/** Creates the backend-for-frontend: sign-in, callback, sign-out and session handling with server-side token storage. */
export function createDovepeakAuth(options: DovepeakAuthOptions): DovepeakAuth {
  const appUrl = new URL(options.appUrl.toString());
  const basePath = (options.basePath ?? "/api/auth").replace(/\/+$/, "");
  const secure = appUrl.protocol === "https:";
  if (!secure && !["localhost", "127.0.0.1"].includes(appUrl.hostname)) {
    throw new ConfigurationError("appUrl must use HTTPS outside local development: session cookies must be Secure.");
  }

  const client = createServerClient({
    ...options.client,
    redirectUri: options.client.redirectUri ?? new URL(`${basePath}/callback`, appUrl),
  });
  const store = options.store;
  const cookieName = secure ? "__Host-dp_session" : "dp_session";
  const txCookie = secure ? "__Host-dp_tx" : "dp_tx";
  const leewayMs = (options.refreshLeewaySeconds ?? 30) * 1000;
  const afterSignIn = options.afterSignInPath ?? "/";
  const afterSignOut = new URL(options.afterSignOutPath ?? "/", appUrl);

  const sessionKey = (id: string) => `session:${id}`;
  const txKey = (id: string) => `tx:${id}`;
  const ttlOf = (tokens: TokenSet) =>
    Math.max(60, Math.floor(((tokens.refreshExpiresAt ?? tokens.expiresAt) - Date.now()) / 1000));

  const readSession = async (id: string): Promise<StoredSession | null> => {
    const value = await store.get(sessionKey(id));
    return value ? (JSON.parse(value) as StoredSession) : null;
  };

  const sessionApi = (read: CookieReader): SessionApi => ({
    async getSession() {
      const id = (await read()).get(cookieName)?.value;
      return id ? ((await readSession(id))?.user ?? null) : null;
    },

    async getAccessToken() {
      const id = (await read()).get(cookieName)?.value;
      if (!id) return null;
      let session = await readSession(id);
      if (!session) return null;
      if (session.tokens.expiresAt - Date.now() > leewayMs) return session.tokens.accessToken;

      const lockKey = `lock:${id}`;
      if (await store.acquire(lockKey, "1", LOCK_MS)) {
        try {
          // Re-read under the lock: another request may have refreshed just before.
          session = (await readSession(id)) ?? session;
          if (session.tokens.expiresAt - Date.now() > leewayMs) return session.tokens.accessToken;
          if (!session.tokens.refreshToken) return null;

          const tokens = await client.refresh(session.tokens.refreshToken);
          const updated: StoredSession = { ...session, tokens: { ...tokens, idToken: tokens.idToken ?? session.tokens.idToken } };
          await store.set(sessionKey(id), JSON.stringify(updated), ttlOf(tokens));
          return tokens.accessToken;
        } catch (error) {
          // The identity provider ended the session (expired, revoked, reuse detected): end ours too.
          if (error instanceof TokenRequestError && error.requiresSignIn) {
            await store.delete(sessionKey(id));
            return null;
          }

          throw error;
        } finally {
          await store.delete(lockKey);
        }
      }

      // Another request is refreshing: wait for its result.
      for (let attempt = 0; attempt < 50; attempt++) {
        await new Promise((resolve) => setTimeout(resolve, 100));
        const latest = await readSession(id);
        if (!latest) return null;
        if (latest.tokens.expiresAt - Date.now() > leewayMs) return latest.tokens.accessToken;
      }

      return null;
    },
  });

  const nextCookies: CookieReader = async () => {
    const { cookies } = await import("next/headers.js");
    return cookies();
  };
  const defaultApi = sessionApi(nextCookies);

  const handler = async (request: Request): Promise<Response> => {
    const url = new URL(request.url);
    const action = url.pathname.slice(url.pathname.lastIndexOf("/") + 1);
    const cookies = parseCookies(request.headers.get("cookie"));

    if (request.method === "GET" && action === "login") {
      const { url: authorizationUrl, transaction } = await client.createAuthorizationRequest({
        register: url.searchParams.get("register") === "1",
        returnTo: safePath(url.searchParams.get("returnTo")) ?? afterSignIn,
      });
      const id = randomId();
      await store.set(txKey(id), JSON.stringify(transaction), TRANSACTION_TTL_SECONDS);
      return redirect(authorizationUrl, [cookie(txCookie, id, TRANSACTION_TTL_SECONDS, secure)]);
    }

    if (request.method === "GET" && action === "callback") {
      const txId = cookies.get(txCookie);
      const stored = txId ? await store.take(txKey(txId)) : null;
      const clearTx = cookie(txCookie, "", 0, secure);
      if (!stored) return redirect(withError(appUrl, "expired"), [clearTx]);

      try {
        const transaction = JSON.parse(stored) as AuthorizationTransaction;
        // Evaluate the callback against the public URL, not an internal host behind a proxy.
        const callback = new URL(url.pathname + url.search, appUrl);
        const { tokens, user } = await client.completeAuthorization(callback, transaction);
        const id = randomId();
        const session: StoredSession = { user: toSessionUser(user), tokens };
        await store.set(sessionKey(id), JSON.stringify(session), ttlOf(tokens));
        return redirect(new URL(transaction.returnTo ?? afterSignIn, appUrl), [clearTx, cookie(cookieName, id, ttlOf(tokens), secure)]);
      } catch {
        // Details are not shown to the browser: they can include identity provider error descriptions.
        return redirect(withError(appUrl, "failed"), [clearTx]);
      }
    }

    if (action === "logout") {
      // POST only, with an Origin check, so another site cannot sign users out (CSRF).
      if (request.method !== "POST" || request.headers.get("origin") !== appUrl.origin) {
        return new Response("Forbidden", { status: 403 });
      }

      const id = cookies.get(cookieName);
      const value = id ? await store.take(sessionKey(id)) : null;
      const session = value ? (JSON.parse(value) as StoredSession) : null;
      if (session?.tokens.refreshToken) await client.revokeSession(session.tokens.refreshToken);
      const target = (session && (await client.endSessionUrl({ idToken: session.tokens.idToken, postLogoutRedirectUri: afterSignOut }))) ?? afterSignOut;
      return redirect(target, [cookie(cookieName, "", 0, secure)], 303);
    }

    if (request.method === "GET" && action === "session") {
      const id = cookies.get(cookieName);
      const session = id ? await readSession(id) : null;
      return Response.json(session ? { authenticated: true, user: session.user } : { authenticated: false }, {
        headers: { "Cache-Control": "no-store" },
      });
    }

    return new Response("Not found", { status: 404 });
  };

  return {
    handler,
    GET: handler,
    POST: handler,
    getSession: defaultApi.getSession,
    getAccessToken: defaultApi.getAccessToken,
    withCookies: sessionApi,
    client,
    cookieName,
    loginPath: (returnTo) => `${basePath}/login${returnTo && safePath(returnTo) ? `?returnTo=${encodeURIComponent(returnTo)}` : ""}`,
  };
}

function toSessionUser(user: UserClaims): SessionUser {
  return {
    sub: user.sub,
    ...(user.email ? { email: user.email } : {}),
    ...(user.name ? { name: user.name } : {}),
  };
}

/** Only same-site absolute paths: an open redirect after sign-in would help phishing. */
function safePath(value: string | null | undefined): string | undefined {
  return value && value.startsWith("/") && !value.startsWith("//") && !value.startsWith("/\\") ? value : undefined;
}

function withError(appUrl: URL, reason: string): URL {
  const url = new URL("/", appUrl);
  url.searchParams.set("error", reason);
  return url;
}

function randomId(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function cookie(name: string, value: string, maxAgeSeconds: number, secure: boolean): string {
  return `${name}=${value}; Path=/; HttpOnly; SameSite=Lax; Max-Age=${maxAgeSeconds}${secure ? "; Secure" : ""}`;
}

function redirect(location: URL, cookies: string[], status = 307): Response {
  const headers = new Headers({ Location: location.toString(), "Cache-Control": "no-store" });
  for (const value of cookies) headers.append("Set-Cookie", value);
  return new Response(null, { status, headers });
}

function parseCookies(header: string | null): Map<string, string> {
  const map = new Map<string, string>();
  for (const part of (header ?? "").split(";")) {
    const index = part.indexOf("=");
    if (index > 0) map.set(part.slice(0, index).trim(), part.slice(index + 1).trim());
  }

  return map;
}
