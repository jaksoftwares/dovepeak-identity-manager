# React single-page app example

A browser-only app (public client) signing users in with Authorization Code + PKCE through `@dovepeak/identity`,
and calling the .NET example API with the access token. Tokens are kept **in memory only**; the PKCE transaction
sits in `sessionStorage` for the redirect and is deleted on use. For web apps that have a server, prefer the
[Next.js backend-for-frontend](../nextjs-bff) — see the [browser threat model](../../docs/sdk/browser-threat-model.md).

## Run it (about 5 minutes)

```bash
docker compose up -d --build --wait                                  # the local stack
dotnet run --project tools/Dovepeak.Identity.DevTool -- demo-setup   # registers demo-spa, writes .env.local
dotnet run --project examples/dotnet-protected-api/Dovepeak.Examples.ProtectedApi -- \
  --Auth:Issuer=http://localhost:8080/realms/dp-demo --Auth:Audience=dovepeak-demo-api \
  --Auth:RequireHttpsMetadata=false --Cors:AllowedOrigins:0=http://localhost:5173
npm install                                                          # at the repository root (workspaces)
npm run dev -w @dovepeak/example-react-spa                           # http://localhost:5173
```

Create an account, confirm the email in Mailpit (http://localhost:8025), then call the protected API.

## Your own app

1. In the developer portal, register a **Single-page app** with the callback `https://your-app/callback`, the logout
   URL `https://your-app/` and your app's origin under allowed origins. Add your API as an audience.
2. Copy the issuer and client ID into `VITE_DOVEPEAK_ISSUER` and `VITE_DOVEPEAK_CLIENT_ID`. These are public values;
   never put a secret in a browser app.
3. Reuse `src/auth.ts`.

`npm run test:e2e` runs the end-to-end test (sign-up, email verification, API call, sign-out) against the stack.
