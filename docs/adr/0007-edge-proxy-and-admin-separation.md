# ADR-0007: Edge Proxy, Rate Limiting and Separation of Administrative Access

**Status:** Proposed
**Date:** 2026-10-09
**Deciders:** Dovepeak Identity maintainers

## Context

The problem statement (§8.6) requires rate limiting on registration, login, recovery, verification and token endpoints, plus email sending quotas. The threat model requires the identity engine's admin API to be unreachable from the internet (T-01, E-01) and the master realm not to be exposed to credential attacks.

Keycloak provides per-account brute-force protection but no per-IP rate limiting or request-level filtering.

## Decision

1. **All public traffic reaches Keycloak through an edge proxy (Traefik v3).**
2. **Administrative surfaces are never routed publicly.** The edge returns 403 for `/admin`, `/realms/master`, `/metrics` and `/health`. The admin console and the Management API reach Keycloak on a separate, private address (`KC_HOSTNAME_ADMIN`; in local development, port 8081 bound to localhost).
3. **Per-IP rate limits, in three tiers**, with counters stored in Valkey so limits hold across multiple edge instances:

   | Tier | Endpoints | Production default |
   | ---- | --------- | ------------------ |
   | Email-sending | Password reset and registration submissions | 5/min, burst 5 |
   | Credentials | All `login-actions` POSTs; token, introspection, logout and revocation endpoints | 30/min, burst 20 |
   | General | Everything else (discovery, JWKS, login pages) | 600/min, burst 200 |

   Limits are environment variables so local development and CI can use higher values (all test traffic comes from one IP).
4. **Access logs never contain request paths.** Keycloak action links (email verification, password reset) carry single-use tokens in the query string, and Traefik cannot redact query strings. The router name, method, status and duration are logged instead.
5. **Keycloak trusts `X-Forwarded-*` headers** (`KC_PROXY_HEADERS=xforwarded`) so its events and brute-force protection see the real client IP. Production deployments must restrict trusted proxies with `KC_PROXY_TRUSTED_ADDRESSES`.

## Consequences

* Defence in depth for credential attacks: per-IP limits at the edge, per-account lockout in Keycloak.
* Email cost abuse (threat model D-02) is bounded per IP. **Per-account** email throttling is not provided by Keycloak and is recorded as a known limitation.
* Clients behind a shared NAT share a rate-limit bucket; limits must be tuned using production metrics.

## Validation

* `EdgeProtectionTests`: administrative paths return 403 publicly; tenant discovery works and carries security headers.
* `EdgeRateLimitTests`: the email-sending tier returns 429 when exceeded.
* `scripts/ci/scan-logs-for-secrets.sh`: found single-use action tokens in edge access logs during Phase 2; fixed by decision 4 and enforced in CI.
