import { randomBytes } from "node:crypto";
import Redis from "ioredis";
import { cookies } from "next/headers";
import { config, secureCookies } from "./config";
import { refreshTokens, type PendingAuthorization, type TokenSet } from "./oidc";

// Backend-for-frontend session store. Tokens live only on the server (in Valkey); the browser holds an opaque,
// HttpOnly session identifier. This keeps access and refresh tokens out of reach of browser JavaScript.

export const SESSION_COOKIE = secureCookies ? "__Host-dp_session" : "dp_session";
export const TRANSACTION_COOKIE = secureCookies ? "__Host-dp_tx" : "dp_tx";

const TRANSACTION_TTL_SECONDS = 600;
const REFRESH_LEEWAY_MS = 30_000;
const REFRESH_LOCK_MS = 10_000;

const globalForRedis = globalThis as unknown as { dovepeakRedis?: Redis };
const redis = (globalForRedis.dovepeakRedis ??= new Redis(config.sessionRedisUrl, { lazyConnect: false }));

export interface SessionUser {
  sub: string;
  email?: string;
  name?: string;
}

export interface Session {
  user: SessionUser;
  tokens: TokenSet;
}

export const cookieOptions = (maxAgeSeconds: number) => ({
  httpOnly: true,
  secure: secureCookies,
  sameSite: "lax" as const,
  path: "/",
  maxAge: maxAgeSeconds,
});

const newId = () => randomBytes(32).toString("base64url");
const sessionKey = (id: string) => `bff:session:${id}`;
const transactionKey = (id: string) => `bff:tx:${id}`;

// ---------------------------------------------------------------- Authorization transactions

export async function saveTransaction(pending: PendingAuthorization): Promise<string> {
  const id = newId();
  await redis.set(transactionKey(id), JSON.stringify(pending), "EX", TRANSACTION_TTL_SECONDS);
  return id;
}

/** Reads and deletes the pending authorization: each one can complete only once. */
export async function takeTransaction(id: string): Promise<PendingAuthorization | null> {
  const value = await redis.getdel(transactionKey(id));
  return value ? (JSON.parse(value) as PendingAuthorization) : null;
}

export { TRANSACTION_TTL_SECONDS };

// ---------------------------------------------------------------- Sessions

export async function createSession(session: Session): Promise<string> {
  const id = newId();
  await redis.set(sessionKey(id), JSON.stringify(session), "EX", session.tokens.refreshTokenExpiresInSeconds);
  return id;
}

export async function deleteSession(id: string): Promise<Session | null> {
  const value = await redis.getdel(sessionKey(id));
  return value ? (JSON.parse(value) as Session) : null;
}

async function readSession(id: string): Promise<Session | null> {
  const value = await redis.get(sessionKey(id));
  return value ? (JSON.parse(value) as Session) : null;
}

/** The current session from the request cookie, without refreshing tokens. */
export async function currentSession(): Promise<{ id: string; session: Session } | null> {
  const id = (await cookies()).get(SESSION_COOKIE)?.value;
  if (!id) return null;
  const session = await readSession(id);
  return session ? { id, session } : null;
}

/**
 * Returns a valid access token, refreshing it if it is about to expire.
 *
 * Refresh tokens are single-use (rotation with reuse detection). If two requests refreshed the same token
 * concurrently, the second would be treated as token theft and the whole session revoked. A per-session lock
 * ensures only one request refreshes; others wait for and reuse its result.
 */
export async function validAccessToken(): Promise<string | null> {
  const current = await currentSession();
  if (!current) return null;

  let { session } = current;
  if (session.tokens.accessTokenExpiresAt - Date.now() > REFRESH_LEEWAY_MS) {
    return session.tokens.accessToken;
  }

  const lockKey = `bff:lock:${current.id}`;
  const lockToken = newId();
  const acquired = await redis.set(lockKey, lockToken, "PX", REFRESH_LOCK_MS, "NX");

  if (acquired) {
    try {
      // Re-read under the lock: another request may have refreshed just before we acquired it.
      session = (await readSession(current.id)) ?? session;
      if (session.tokens.accessTokenExpiresAt - Date.now() > REFRESH_LEEWAY_MS) {
        return session.tokens.accessToken;
      }

      const tokens = await refreshTokens(session.tokens.refreshToken, session.tokens.idToken);
      const updated: Session = { ...session, tokens };
      await redis.set(sessionKey(current.id), JSON.stringify(updated), "EX", tokens.refreshTokenExpiresInSeconds);
      return tokens.accessToken;
    } catch {
      // Refresh failed (session expired, revoked or reuse detected): end the local session too.
      await deleteSession(current.id);
      return null;
    } finally {
      // Release only our own lock.
      if ((await redis.get(lockKey)) === lockToken) await redis.del(lockKey);
    }
  }

  // Another request is refreshing: wait for it to finish, then use the new token.
  for (let attempt = 0; attempt < 50; attempt++) {
    await new Promise((resolve) => setTimeout(resolve, 100));
    const latest = await readSession(current.id);
    if (!latest) return null;
    if (latest.tokens.accessTokenExpiresAt - Date.now() > REFRESH_LEEWAY_MS) return latest.tokens.accessToken;
  }
  return null;
}
