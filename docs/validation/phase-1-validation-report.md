# Phase 1 Validation Report — Identity Engine (Keycloak 26.4.0)

**Milestone:** M1.6 (Gate G1)
**Date:** 2026-10-09
**Environment:** Local Docker stack on a developer workstation (Windows 11, Docker Desktop, 7.5 GiB available to containers). Single Keycloak node, `start-dev` mode, PostgreSQL 17.6.
**Recommendation:** **GO** with the conditions in section 6.

Production numbers will differ (clustered Keycloak, production mode, dedicated database). These measurements establish **trends and limits**, not production capacity.

---

## 1. Summary

| Question | Answer | Evidence |
| -------- | ------ | -------- |
| Can tenant realms be provisioned programmatically and idempotently? | Yes | `RealmProvisioningTests` |
| Can the Management API's Keycloak account be least-privilege (threat model E-01)? | Yes — only master `create-realm`; admin rights over its own realms only, 403 elsewhere | `RealmProvisioningTests`, manual probe |
| Does Authorization Code + PKCE work end to end, including a BFF? | Yes | `AuthorizationCodeFlowTests`, `tests/e2e/bff-smoke.mjs` (12/12) |
| Can a .NET resource server validate tokens and reject forgeries? | Yes — `alg: none`, tampering, HS256 key confusion, wrong audience, other tenant, expiry all rejected | `TokenValidationTests` |
| Refresh rotation and reuse detection? | Yes — reuse revokes the client session | `RefreshTokenRotationTests`, ADR-0003 |
| One theme for all tenants with tenant-specific branding? | Yes (tenant name); logos and colours deferred | `HostedLoginThemeTests`, ADR-0006 |
| Does the platform scale to hundreds of realms on one cluster? | Yes for runtime traffic; provisioning slows linearly and memory grows per realm | Section 4 |

## 2. Findings That Changed the Design

| # | Finding | Action taken |
| - | ------- | ------------ |
| F-1 | Keycloak's default user profile requires first and last name, forcing an "update profile" step after registration. | Template applies `user-profile.json` with optional names. |
| F-2 | Keycloak's `reject-ropc-grant` client policy executor throws a server error when `auto-configure` is omitted. | Template sets `auto-configure: true` on every executor. |
| F-3 | Keycloak stores only a subset of event types by default; refresh-token errors were not recorded. | Template lists security event types explicitly. |
| F-4 | **Admin token growth:** the service account's token grew ~475 bytes per realm (11 KB at 20 realms; ~240 KB projected at 500), which would exceed HTTP header limits. | Bootstrap removes claim-producing scopes; token is a constant **894 bytes** at any realm count. Keycloak evaluates admin rights from stored role mappings, so least privilege is unaffected. |
| F-5 | **Concurrency bug:** a token fetched before a realm existed could be cached after the realm's creator invalidated the cache, causing 403 on the new realm. Exposed by parallel integration tests. | Generation-checked token cache; regression unit test. |
| F-6 | Keycloak marks cookies `Secure` even on `http://localhost`. | Test harness manages cookies itself; no product change. |
| F-7 | **Secret leak in logs:** the edge access log recorded single-use action tokens from email verification and password reset links. | Request paths dropped from access logs (ADR-0007); CI log scanner enforces it. |

## 3. Functional Validation

Automated suites against the live stack — 86 integration tests, all passing:

| Suite | Tests | Covers |
| ----- | ----: | ------ |
| `RealmProvisioningTests` | 13 | Idempotent realm/client lifecycle, least privilege, constant-size admin token, secure client defaults, redirect URI rules |
| `RealmBaselineTests` | 21 | Every security default in the template, security event types, security headers |
| `ClientPolicyTests` | 4 | PKCE required, unregistered redirect rejected without redirect, password and implicit grants rejected |
| `AuthorizationCodeFlowTests` | 6 | Public and confidential clients, wrong verifier, code replay, generic errors |
| `TokenValidationTests` | 8 | Resource server validation and every forgery case |
| `RefreshTokenRotationTests` | 5 | Rotation, reuse detection, logout, admin logout, single-session revocation |
| `RegistrationAndVerificationTests` | 4 | Registration, password policy, email verification |
| `PasswordRecoveryTests` | 3 | Recovery, session revocation, enumeration resistance, single-use links |
| `BruteForceProtectionTests` | 2 | Lockout and lockout events |
| `SessionLifetimeTests` | 2 | Absolute session lifetime |
| `SigningKeyRotationTests` | 3 | Planned rotation, retirement, emergency rotation |
| `AuditCollectionTests` | 4 | Authentication and admin events reach the audit store safely, idempotently |
| `EdgeProtectionTests` / `EdgeRateLimitTests` | 8 | Admin surfaces blocked publicly, security headers, rate limiting |
| `HostedLoginThemeTests` | 3 | Theme applied, tenant names, HTML encoding |

## 4. Scale Test

`dovepeak-dev scale-test --realms 500`: each realm gets the full secure template plus one machine client. Latency is sampled through the public edge across up to 20 realms at each checkpoint.

| Realms | Provision p50 ms | Provision p95 ms | Token p50 ms | Token p95 ms | Discovery p95 ms | Admin token bytes |
| -----: | ---------------: | ---------------: | -----------: | -----------: | ---------------: | ----------------: |
|    50 |     1669 |     5277 |       59 |       94 |        23 |        894 |
|   100 |     1881 |     3085 |       27 |       35 |         8 |        894 |
|   150 |     3427 |     5360 |       30 |       40 |         6 |        894 |
|   200 |     4032 |     7021 |       65 |       97 |        19 |        894 |
|   250 |     4995 |     9056 |       53 |       73 |        23 |        894 |
|   300 |     6829 |    10302 |       34 |       77 |        25 |        894 |
|   350 |     6752 |    10973 |       47 |      194 |        15 |        894 |
|   400 |     8402 |    11734 |       55 |      161 |        47 |        894 |
|   450 |     9544 |    12672 |       48 |       72 |        26 |        894 |
|   500 |     8951 |    11817 |       54 |       93 |        24 |        894 |

**Keycloak memory:** 1.10 GiB before provisioning, 2.78 GiB with 500 tenant realms (about 3.4 MiB per realm). Keycloak used about 180% CPU while provisioning and returned to idle afterwards.

### Interpretation

* **Runtime traffic stays fast.** Token issuance and discovery latency stay in the tens of milliseconds as realms accumulate. Sign-in performance is not the constraint.
* **Provisioning slows roughly linearly with realm count.** Creating a realm takes longer as each one is added, because Keycloak administers the service account's growing set of per-realm admin roles and realm caches. Provisioning is an asynchronous, infrequent operation, so this affects onboarding time, not users.
* **Memory grows per realm.** Each realm keeps caches, keys and configuration in memory.

### Sharding threshold

Per ADR-0001, tenants are assigned to Keycloak clusters through a `tenant → keycloak_cluster` mapping. Based on these measurements:

* **Initial threshold: 300 realms per cluster** (100 projects with 3 environments each). At 300 realms, provisioning takes ~7 seconds per realm (p50) on this hardware and Keycloak memory has grown by roughly 1 GiB. Beyond that, provisioning keeps slowing (~9 seconds at 500) while sign-in latency is unaffected.
* The threshold is a configuration value, to be re-measured on production hardware in M6.4 (load testing).

## 5. Licence Review

| Component | Licence | Use | Compatible with Apache-2.0 distribution |
| --------- | ------- | --- | --------------------------------------- |
| Keycloak 26.4 | Apache-2.0 | Identity engine (unmodified image) | Yes |
| PostgreSQL 17 | PostgreSQL Licence | Database | Yes |
| Valkey 8.1 | BSD-3-Clause | Cache, rate-limit counters, BFF sessions | Yes |
| Traefik 3.5 | MIT | Edge proxy | Yes |
| Mailpit | MIT | Local development only | Yes (not distributed in production) |
| .NET 10, ASP.NET Core, EF Core | MIT | Management API, workers | Yes |
| Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL Licence | Database driver | Yes |
| EFCore.NamingConventions | Apache-2.0 | snake_case schema | Yes |
| AspNetCore.HealthChecks.* | Apache-2.0 | Health checks | Yes |
| Next.js 16, React 19 | MIT | BFF example | Yes |
| oauth4webapi | MIT | OIDC client in the BFF example | Yes |
| ioredis | MIT | Session store client | Yes |
| Keycloakify | MIT | Not used yet (ADR-0006) | Yes |
| Ory Kratos / Hydra (rejected) | Apache-2.0 | — | — |
| Redis 7.4+ (rejected) | RSALv2 / SSPLv1 / AGPLv3 | — | Replaced by Valkey (ADR-0005) |

## 6. Recommendation

**GO: adopt Keycloak (ADR-0001)**, subject to these conditions:

1. **Constant-size admin token** (F-4) stays in the bootstrap and is covered by a test before Phase 3.
2. **Tenant-to-cluster mapping** is part of the Phase 3 data model from the start (M3.4), with the threshold in section 4 as configuration.
3. **Provisioning is asynchronous** in the Management API (outbox, M3.7). It must never block an HTTP request while Keycloak creates a realm.
4. **Keycloak version upgrades** run the full integration suite before adoption.
5. **Re-measure at production scale** in M6.4 with clustered Keycloak in production mode.

The fallback (OpenIddict, ADR-0001 option 4) is not needed.
