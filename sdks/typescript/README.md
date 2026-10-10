# @dovepeak/identity

TypeScript SDK for [Dovepeak Identity](https://github.com/jaksoftwares/dovepeak-identity-manager): OpenID Connect
sign-in, a backend-for-frontend for Next.js, React hooks, and access token verification for Node.js APIs.

```bash
npm install @dovepeak/identity
```

| Import | Use it in | What it does |
| ------ | --------- | ------------ |
| `@dovepeak/identity` | Browser or server | OIDC client: Authorization Code + PKCE, refresh, client credentials, logout URL |
| `@dovepeak/identity/next` | Next.js server | Backend-for-frontend: sign-in, callback, sign-out and session routes; server-side sessions |
| `@dovepeak/identity/react` | React (client) | `DovepeakProvider`, `useSession`, `useAuth`, `<SignedIn>` backed by the BFF |
| `@dovepeak/identity/node` | APIs | Verify access tokens; check scopes and roles |

Copy the issuer and client ID from the application's **Integration** tab in the developer portal.

## Next.js (recommended for web apps)

Tokens stay on your server; the browser only holds an `HttpOnly` session cookie.

```ts
// lib/auth.ts
import Redis from "ioredis";
import { cookies } from "next/headers";
import { createDovepeakAuth, redisSessionStore } from "@dovepeak/identity/next";

export const auth = createDovepeakAuth({
  client: { issuer: process.env.DOVEPEAK_ISSUER!, clientId: process.env.DOVEPEAK_CLIENT_ID!, clientSecret: process.env.DOVEPEAK_CLIENT_SECRET },
  appUrl: process.env.APP_URL!,                  // https://shop.example.com
  store: redisSessionStore(new Redis(process.env.SESSION_REDIS_URL!)),
});
export const session = () => auth.withCookies(cookies);
```

```ts
// app/api/auth/[...dovepeak]/route.ts   →  /api/auth/login, /callback, /logout (POST), /session
import { auth } from "@/lib/auth";
export const { GET, POST } = auth;
```

```tsx
// a server component
const user = await session().getSession();               // null when signed out
const token = await session().getAccessToken();          // refreshed automatically, one refresh at a time
```

```tsx
// a client component
"use client";
import { DovepeakProvider, useAuth, useSession } from "@dovepeak/identity/react";

function Account() {
  const { status, user } = useSession();
  const { signIn, signOut } = useAuth();
  return status === "authenticated"
    ? <button onClick={signOut}>Sign out {user.email}</button>
    : <button onClick={() => signIn()}>Sign in</button>;
}
```

Register the callback `https://your-app/api/auth/callback` and the logout URL `https://your-app/` for the application.
Sign-out is a `POST` checked against your app's origin (CSRF); serve the app with `Referrer-Policy: same-origin`
(not `no-referrer`, which makes browsers send `Origin: null`).

## Single-page apps

```ts
import { createBrowserClient } from "@dovepeak/identity";

const client = createBrowserClient({ issuer, clientId, redirectUri: `${location.origin}/callback` });
const { url, transaction } = await client.createAuthorizationRequest();
sessionStorage.setItem("tx", JSON.stringify(transaction));   // deleted on the callback
location.assign(url);
// on /callback:
const { tokens, user } = await client.completeAuthorization(location.href, JSON.parse(sessionStorage.getItem("tx")!));
```

Keep tokens in memory. `createBrowserClient` refuses a client secret. See the
[browser threat model](../../docs/sdk/browser-threat-model.md).

## Protect an API (Node.js)

```ts
import { createTokenVerifier, errorResponse, requirePermissions } from "@dovepeak/identity/node";

const verifier = createTokenVerifier({ issuer, audience: "orders-api" });

app.get("/orders", async (req, res) => {
  try {
    const token = await verifier.verifyAuthorizationHeader(req.headers.authorization);
    requirePermissions(token, { scopes: ["orders:read"] });   // or roles: ["administrator"]
    res.json(await listOrders(token.subject));
  } catch (error) {
    const { status, headers } = errorResponse(error);         // 401 invalid token, 403 missing permission
    res.status(status).set(headers).end();
  }
});
```

Only RS256 is accepted; issuer, audience, expiry and signature are always checked. Pass
`introspection: { clientId, clientSecret }` to reject revoked sessions immediately.

## Errors

Every error is a `DovepeakError` with a stable `code`: `ConfigurationError`, `AuthorizationError` (callback),
`TokenRequestError` (`requiresSignIn` for an ended session), `TokenVerificationError` (`reason`: `expired`,
`audience`, `issuer`, `signature`, …), `ForbiddenError` and `NetworkError`. Messages never contain tokens.

## License

Apache-2.0
