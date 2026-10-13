# Supported Flows and Security Defaults

What Dovepeak Identity 1.0 supports, what it deliberately rejects, and the defaults every tenant realm gets. Everything listed as supported is covered by automated tests unless marked otherwise. Limitations are in [known-limitations.md](security/known-limitations.md).

## Application types and flows

| Application type (portal / API `kind`) | Flow | Client authentication | Notes |
| -------------------------------------- | ---- | --------------------- | ----- |
| Web application / BFF (`web`) | Authorization Code + PKCE (S256) | Client secret (rotatable with a 24-hour overlap) | Recommended for browser apps: tokens stay on the server ([browser threat model](sdk/browser-threat-model.md)) |
| Single-page application (`spa`) | Authorization Code + PKCE (S256) | None (public client) | Tokens in memory only; allowed origins required |
| Native or mobile application (`native`) | Authorization Code + PKCE (S256) | None (public client) | Custom-scheme (`com.example.app:/callback`) or loopback (`http://127.0.0.1:port/…`) redirect URIs |
| Machine-to-machine (`machine`) | Client Credentials | Client secret | No end user; OAuth scopes and audiences control access |

Every interactive application also gets:

* **Refresh tokens with rotation** — each refresh returns a new refresh token; reusing an old one is rejected and revokes the whole token family (ADR-0003).
* **RP-initiated logout** to registered post-logout redirect URIs, and session revocation from the portal or the Management API.
* **Token introspection** for resource servers that need immediate revocation (SDK introspection mode).

### Rejected

| Flow | Why |
| ---- | --- |
| Implicit grant | Tokens in URLs; replaced by Authorization Code + PKCE. Rejected by client policy in every realm. |
| Resource Owner Password Credentials | Applications would see user passwords. Rejected by client policy. |
| Authorization Code without PKCE | Code interception. PKCE (S256) is enforced by client policy. |
| Device Authorization Grant, CIBA | Disabled on every client; not in the 1.0 scope. |
| Wildcard or partial redirect URIs | Open-redirect and code theft. Rejected at registration; exact matching only. |
| Dynamic client registration, token exchange | Not offered; applications are registered through the portal or the Management API. |

## End-user features

| Feature | Default |
| ------- | ------- |
| Self-registration | On; email verification required before first sign-in |
| Password recovery | By email; link valid 15 minutes and usable once |
| Password policy | At least 12 characters (max 128), not the username or email, not one of the last 5; hashed with Argon2 |
| Brute-force protection | Temporary lockout after 5 failures, growing by 1 minute per failure up to 15 minutes; never permanent |
| Security alert emails | Password changed, account locked |
| Sign-in identifiers | Email address (unique per realm) |
| Branding | Logo, brand colour, email subjects and introductions per project (ADR-0010) |
| Second factor (TOTP) | Users can add an authenticator app in Keycloak's account console. Not enforced per tenant and **not yet covered by automated tests** |

Deferred until after 1.0: passkeys, social sign-in, enterprise SSO (SAML/OIDC federation), custom domains, enforced MFA policies.

## Tokens and sessions

| Setting | Default | Per application |
| ------- | ------- | --------------- |
| Access token lifetime | 10 minutes | 5–60 minutes |
| Session idle timeout | 30 minutes | 5–30 minutes |
| Session maximum lifetime | 12 hours | 5 minutes – 12 hours |
| Signing algorithm | RS256 (asymmetric only) | — |
| Authorization code lifetime | 60 seconds, single use | — |

Access tokens carry `iss`, `aud` (the application's declared audiences), `azp`, `sub`, `scope` (granted OAuth scopes) and `roles` (the application's roles for the user). The SDKs validate issuer, audience, signature (RS256 only), expiry and these claims.

## Platform guarantees

* **Tenant isolation** — every Management API endpoint is covered by the tenant-isolation suite; PostgreSQL row-level security enforces it in the database as well (ADR-0008).
* **Configuration integrity** — direct changes to the identity engine are reverted and audited within the reconciliation interval (5 minutes), and alerted on.
* **Secrets** — client secrets and API keys are shown once; API keys are stored as keyed digests; nothing secret appears in logs, error responses or public client configuration.
* **Email abuse limits** — per-IP limits at the edge, and per-recipient (5 per hour, 20 per day) and per-tenant (500 per hour) quotas in the mail relay.
* **Audit** — authentication and administrative events are collected per tenant and retained 365 days.

## Objectives

Availability 99.9%, sign-in p95 under 300 ms, RPO 15 minutes, RTO 1 hour: [service-level-objectives.md](operations/service-level-objectives.md).
