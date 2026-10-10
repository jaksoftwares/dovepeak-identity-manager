// @dovepeak/identity/next: backend-for-frontend for Next.js (App Router).
//
//   // lib/auth.ts
//   export const auth = createDovepeakAuth({ client: {...}, appUrl: process.env.APP_URL!, store: redisSessionStore(redis) });
//   // app/api/auth/[...dovepeak]/route.ts
//   export const { GET, POST } = auth;
//
// Tokens are stored server-side; the browser holds only an opaque HttpOnly session cookie.
export { createDovepeakAuth } from "./auth.js";
export type { CookieReader, DovepeakAuth, DovepeakAuthOptions, SessionApi, SessionUser } from "./auth.js";
export { memorySessionStore, redisSessionStore } from "./session-store.js";
export type { RedisLike, SessionStore } from "./session-store.js";
