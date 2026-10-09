# Example: Next.js Backend-for-Frontend (BFF)

A Next.js application that signs users in with Dovepeak Identity using **Authorization Code with PKCE** and keeps every token **on the server**. The browser only ever holds an opaque, `HttpOnly` session cookie.

This is the recommended pattern for browser applications (problem statement §8.2). It is also the reference implementation for the `@dovepeak/identity/next` adapter (milestone M5.2).

## How it works

```text
Browser ──(session cookie)──▶ Next.js BFF ──(tokens)──▶ Protected API
   │                             │  ▲
   │  redirect                   │  │ code + PKCE verifier + client secret
   ▼                             ▼  │
 Dovepeak hosted login  ─────────────┘
```

| Route | Purpose |
| ----- | ------- |
| `GET /api/auth/login` | Starts sign-in (`?register=1` for registration). Stores PKCE verifier, `state` and `nonce` server-side. |
| `GET /api/auth/callback` | Validates `state` and `iss`, exchanges the code, validates the ID token and `nonce`, creates the session. |
| `POST /api/auth/logout` | Origin-checked (CSRF). Revokes the session server-side, then ends the identity provider session. |
| `GET /api/me` | Current user profile. Never returns tokens. |
| `GET /api/protected` | Calls the .NET example API with the user's access token, refreshing it when needed. |

### Security properties

* **No tokens in the browser.** Access, refresh and ID tokens live in Valkey, keyed by a 256-bit random session ID.
* **Single-use authorization transactions.** The PKCE verifier and `state` are deleted when the callback reads them.
* **Safe refresh under concurrency.** Refresh tokens rotate and reuse is treated as theft (ADR-0003). A per-session lock makes sure only one request refreshes; concurrent requests wait for its result instead of reusing the old token and getting the session revoked.
* **Cookies:** `HttpOnly`, `SameSite=Lax`, `Secure` and the `__Host-` prefix when served over HTTPS.
* **Headers:** `frame-ancestors 'none'`, `X-Frame-Options: DENY`, `nosniff`, `no-referrer`.

## Run locally

Prerequisites: the local stack is running (`docker compose up -d --wait` from the repository root).

```bash
# 1. Provision the demo tenant, BFF client and demo user; writes .env.local
dotnet run --project tools/Dovepeak.Identity.DevTool -- demo-setup

# 2. Start the protected API (separate terminal)
dotnet run --project examples/dotnet-protected-api/Dovepeak.Examples.ProtectedApi -- \
  --Auth:Issuer=http://localhost:8080/realms/dp-demo \
  --Auth:Audience=dovepeak-demo-api --Auth:RequireHttpsMetadata=false

# 3. Start the app (separate terminal)
cd examples/nextjs-bff
npm install
npm run dev
```

Open http://localhost:3000, then create an account (the verification email appears in Mailpit at http://localhost:8025) or use the demo user printed by `demo-setup`.

## End-to-end test

```bash
node tests/e2e/bff-smoke.mjs
```

It registers a new user through the app, verifies the email, calls the protected API, checks that no tokens reach the browser, and verifies that cross-site logout is rejected and real logout ends the session. It runs in CI on every pull request.

## Configuration

All variables are **server-side only** (see `.env.example`). None use the `NEXT_PUBLIC_` prefix.

| Variable | Description |
| -------- | ----------- |
| `DOVEPEAK_ISSUER` | Tenant issuer URL |
| `DOVEPEAK_CLIENT_ID` | Confidential client ID |
| `DOVEPEAK_CLIENT_SECRET` | Client secret (shown once when the client is created) |
| `APP_URL` | Public URL of this app; used for redirect URIs and the logout Origin check |
| `SESSION_REDIS_URL` | Valkey/Redis URL for server-side sessions |
| `PROTECTED_API_URL` | Base URL of the protected API |
