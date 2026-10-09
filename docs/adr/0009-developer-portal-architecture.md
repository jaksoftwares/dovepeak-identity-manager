# ADR-0009: Developer Portal Architecture

**Status:** Proposed
**Date:** 2026-10-09
**Deciders:** Dovepeak Identity maintainers

## Context

Phase 4 delivers the developer portal: sign-in for developers, and every Management API capability (organizations, members, projects, applications, credentials, scopes, roles, end-user sessions, API keys, webhooks, audit log) plus integration guidance. The portal handles the most sensitive material the platform shows: client secrets, API keys and webhook signing secrets, each displayed exactly once (ADR-0004).

## Decision

1. **Backend-for-frontend.** The portal is a Next.js server (App Router, React Server Components, Server Actions). It signs developers in through the platform realm's public `dovepeak-portal` client with Authorization Code + PKCE. Tokens are held server-side in Valkey; the browser holds only an opaque `HttpOnly`, `SameSite=Lax` session cookie (`__Host-` prefixed over HTTPS). This is the same pattern as the BFF example and the guidance in the documentation.
2. **The Management API is the only authority.** Pages and actions call the API as the signed-in developer. The portal hides controls a role cannot use, but never decides access itself: the API's authorization and tenant isolation (ADR-0008) apply to every portal request.
3. **One-time secrets live only in the action response.** They are rendered once by the action's result and are never stored, cached or logged by the portal. Create requests carry an `Idempotency-Key`.
4. **Documentation ships inside the portal** (`/docs`) instead of a separate Docusaurus or Nextra site. Guides are versioned with the code, the API reference is rendered from the live OpenAPI document (so it cannot drift), and no extra deployment is needed. A standalone documentation site can be split out later without changing the content.
5. **Runtime configuration.** One image serves every environment; configuration is read at request time. In containers, server-side calls to the identity engine use `IDENTITY_INTERNAL_URL` while browsers are redirected to the public issuer.
6. **Security headers:** `frame-ancestors 'none'`, `X-Frame-Options: DENY`, `nosniff`, a `form-action` allow-list and `Referrer-Policy: same-origin` (not `no-referrer`, which makes browsers send `Origin: null` on form POSTs and defeats the sign-out CSRF check).

## Consequences

- **Positive:** no token ever reaches browser JavaScript; the portal adds no new authorization surface; the API reference always matches the deployed API.
- **Positive:** the end-to-end test (`portal/e2e`, Playwright) exercises the whole developer journey in a real browser against the deployed stack.
- **Negative:** the portal depends on Valkey for sessions; losing Valkey signs developers out (no data loss).
- **Negative:** documentation search and versioning are basic until a dedicated documentation site is justified.
