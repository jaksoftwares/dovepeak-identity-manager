# Browser Threat Model and Token Storage

How the Dovepeak SDKs keep tokens away from attackers in the browser (milestone M5.2). The recommended architecture
for web applications is a **backend-for-frontend (BFF)**; single-page apps without a server are supported with the
trade-offs stated below.

## Threats

| ID | Threat | BFF (`@dovepeak/identity/next` + `/react`) | Single-page app (`createBrowserClient`) |
| -- | ------ | ------------------------------------------ | --------------------------------------- |
| B-01 | **XSS steals tokens** from storage | Not possible: tokens never reach the browser. The session cookie is `HttpOnly`. | Tokens are kept **in memory only** (never `localStorage`), so a one-off script cannot read past sessions. A persistent XSS can still use the in-memory token while the page is open. Use a strict CSP. |
| B-02 | **XSS calls APIs as the user** | Possible through the BFF while the page is open (inherent to any XSS); limited to the BFF's routes. | Possible while the page is open. |
| B-03 | **Authorization code interception** | PKCE (S256) with a server-held verifier; `state` and `nonce` validated; exact redirect URIs. | PKCE with the verifier in `sessionStorage` for the few seconds of the redirect, deleted on use; `state`, `nonce` and issuer validated. |
| B-04 | **CSRF** (forced sign-out, login CSRF) | Sign-out is `POST` with an `Origin` check; cookies are `SameSite=Lax`; sign-in transactions are single-use and bound to a cookie. | Sign-in state is bound to the tab (`sessionStorage`); sign-out is a navigation to the identity provider. |
| B-05 | **Refresh token theft and replay** | Refresh tokens stay on the server; rotation with reuse detection ends a stolen family; one refresh at a time per session. | Refresh tokens in memory; rotation with reuse detection; one refresh at a time. Prefer short sessions. |
| B-06 | **Open redirect after sign-in** | `returnTo` accepts only same-site paths. | The app chooses the return path. |
| B-07 | **Secret in the bundle** | The client secret is server-side only; `createServerClient` throws in a browser. | `createBrowserClient` rejects a client secret at the type level and at runtime. |
| B-08 | **Clickjacking** of sign-in or account pages | Hosted pages and the examples send `frame-ancestors 'none'` and `X-Frame-Options: DENY`. | Same headers on the SPA host (see the example's `vite.config.ts`). |

## Storage behaviour

| Data | BFF | Single-page app |
| ---- | --- | --------------- |
| Access, refresh and ID tokens | Server-side session store (Redis/Valkey), keyed by an opaque random session ID | JavaScript memory; lost when the tab closes |
| Browser state | `dp_session` cookie: `HttpOnly`, `SameSite=Lax`, `Secure` and `__Host-` prefix over HTTPS | None persisted |
| Sign-in transaction (PKCE verifier, state, nonce) | Session store for 10 minutes, single use, bound to an `HttpOnly` cookie | `sessionStorage` until the callback, then deleted |

## Required headers for apps using the SDK

- `Content-Security-Policy` with at least `frame-ancestors 'none'; base-uri 'self'; object-src 'none'`, and a
  `form-action` that allows your identity provider (sign-out redirects follow a form POST).
- `Referrer-Policy: same-origin`. **Not** `no-referrer`: with it, browsers send `Origin: null` on form posts and the
  BFF rejects sign-out.
- `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`.

## Residual risks

- Any XSS can act as the user while the page is open, whatever the storage. Defence: CSP, output encoding,
  dependency hygiene.
- Locally validated access tokens remain valid until they expire (10 minutes by default) after a session is revoked
  (limitation L-01). APIs that need immediate revocation enable introspection in the SDK.
