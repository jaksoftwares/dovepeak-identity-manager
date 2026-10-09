# Dovepeak Identity — Implementation Plan and Milestones

**Document Type:** Implementation Plan
**Companion documents:** [problem-statement.md](problem-statement.md) · [implementation-phases.md](implementation-phases.md)
**Status:** Draft for team review

This plan breaks the seven implementation phases into ordered, trackable milestones. Each milestone has an identifier, its dependencies, a scope checklist and a "done when" condition. Architecture decisions and the reasoning behind them are in [implementation-phases.md](implementation-phases.md).

Week numbers assume a team of 3–5 engineers starting in week 1 and should be re-baselined after Phase 1.

---

# 1. Plan Overview

## 1.1 Phase Schedule

| Phase | Name                                  | Weeks    | Milestones   |
| ----- | ------------------------------------- | -------- | ------------ |
| 0     | Project Foundations                   | 1–2      | M0.1 – M0.4  |
| 1     | Architecture and Technical Validation | 3–5      | M1.1 – M1.6  |
| 2     | Core Identity Platform                | 6–11     | M2.1 – M2.6  |
| 3     | Management API and Multi-Tenancy      | 12–18    | M3.1 – M3.9  |
| 4     | Developer Portal                      | 15–21    | M4.1 – M4.6  |
| 5     | SDKs and Developer Experience         | 19–24    | M5.1 – M5.6  |
| 6     | Production Readiness                  | 25–30    | M6.1 – M6.6  |

```text
Week:     1   3   5   7   9   11  13  15  17  19  21  23  25  27  29
Phase 0  ██
Phase 1      ███
Phase 2         ██████
Phase 3                ███████
Phase 4                   ███████
Phase 5                       ██████
Phase 6                             ██████
```

## 1.2 Release Gates

| Gate           | Week | Requires                    | Audience                                    |
| -------------- | ---- | --------------------------- | ------------------------------------------- |
| **G1 — Go/No-Go** | 5  | M1.6                        | Internal decision on the identity engine    |
| **G2 — Alpha**    | 18 | Phase 2 + Phase 3 complete  | Dovepeak internal applications (dogfooding) |
| **G3 — Beta**     | 24 | Phase 4 + Phase 5 complete  | Selected external developers                |
| **G4 — v1.0**     | 30 | Phase 6 complete            | Public open-source release                  |

No gate may be passed until every milestone it requires is complete.

## 1.3 Milestone Dependency Map

```text
M0.1 ─▶ M0.2 ─▶ M0.3 ─▶ M0.4
                  │
                  ▼
M1.1 ─▶ M1.2 ─▶ M1.3 ─▶ M1.4 ─▶ M1.6 (G1)
          └──▶ M1.5 ──────────────┘
                                   │
                                   ▼
M2.1 ─▶ M2.2 ─▶ M2.3 ─▶ M2.4 ─▶ M2.5 ─▶ M2.6
                                          │
                                          ▼
M3.1 ─▶ M3.2 ─▶ M3.3 ─▶ M3.4 ─▶ M3.5 ─▶ M3.6 ─▶ M3.7 ─▶ M3.8 ─▶ M3.9 (G2)
          │                │
          ▼                ▼
        M4.1 ─▶ M4.2 ─▶ M4.3 ─▶ M4.4 ─▶ M4.5 ─▶ M4.6
                           │
                           ▼
                M5.1 ─▶ M5.2 ─▶ M5.3 ─▶ M5.4 ─▶ M5.5 ─▶ M5.6 (G3)
                                                          │
                                                          ▼
                M6.1 ─▶ M6.2 ─▶ M6.3 ─▶ M6.4 ─▶ M6.5 ─▶ M6.6 (G4)
```

---

# 2. Phase 0 — Project Foundations (Weeks 1–2)

## M0.1 — Repository and Governance

**Depends on:** —

- [x] Create the monorepo with the agreed layout (`/docs`, `/services`, `/identity`, `/portal`, `/sdks`, `/examples`, `/deploy`, `/tests`, `/branding`).
- [x] Add `LICENSE` (Apache-2.0), `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md` and `SECURITY.md`.
- [x] Add pull request and issue templates.
- [ ] Define branch protection rules and the branching strategy.
- [ ] Create GitHub milestones matching this document.

**Done when:** the repository is initialised and the governance files are reviewed.

## M0.2 — CI Pipeline and Security Scanning

**Depends on:** M0.1

- [x] GitHub Actions workflow for build, lint and unit tests.
- [x] CodeQL static analysis.
- [x] Trivy dependency and container scanning.
- [x] Gitleaks secret scanning.
- [x] Dependabot configuration.
- [ ] Merging is blocked on any failing check.

**Done when:** a pull request with a planted test secret is blocked by CI.

## M0.3 — Local Development Stack

**Depends on:** M0.2

- [x] Docker Compose with PostgreSQL, Redis, Keycloak and Mailpit.
- [x] Management API skeleton with `/health` and `/ready` endpoints.
- [x] `.env.example` with no real secrets.
- [x] README "Getting started" section.

**Done when:** a new engineer runs the full stack from the README alone in under 30 minutes.

## M0.4 — Architecture Decisions and Threat Model

**Depends on:** M0.3

- [x] ADR-001 Identity engine selection.
- [x] ADR-002 Identity ownership and sharing model.
- [x] ADR-003 Token lifetime and revocation semantics.
- [x] ADR-004 Credential types.
- [x] Initial STRIDE threat model with trust-boundary diagram.

**Done when:** all ADRs and the threat model are reviewed and merged into `/docs`.

---

# 3. Phase 1 — Architecture and Technical Validation (Weeks 3–5)

## M1.1 — Realm Provisioning Spike

**Depends on:** M0.4

- [ ] Create a realm through the Keycloak Admin API from prototype .NET code.
- [ ] Register a public SPA client and a confidential backend client.
- [ ] Delete a realm and confirm clean removal.

**Done when:** realm creation and deletion are fully scripted and repeatable.

## M1.2 — End-to-End Login Proof of Concept

**Depends on:** M1.1

- [ ] Throwaway Next.js app using Authorization Code with PKCE through a BFF.
- [ ] Registration, login and logout.
- [ ] Email verification through Mailpit.
- [ ] Password recovery.
- [ ] Session cookie is HTTP-only, Secure and protected against CSRF.

**Done when:** a user completes the full account lifecycle in the proof-of-concept app.

## M1.3 — Protected API Token Validation

**Depends on:** M1.2

- [ ] .NET API validating issuer, audience, signature, expiry and required claims.
- [ ] Rejects unsigned tokens, `alg: none` and unexpected algorithms.
- [ ] Rejects tokens from a different realm.

**Done when:** automated tests prove valid tokens pass and every invalid case is rejected.

## M1.4 — Refresh Rotation and Reuse Detection

**Depends on:** M1.3

- [ ] Refresh tokens rotate on every use.
- [ ] Reusing a rotated refresh token revokes the session.
- [ ] Measured behaviour documented against ADR-003.

**Done when:** a reuse attack in a test is detected and the session is revoked.

## M1.5 — Branded Login Theme

**Depends on:** M1.2

- [ ] Keycloakify theme using the Dovepeak brand guidelines.
- [ ] Login, registration, verification and recovery pages styled.
- [ ] Prototype of per-tenant branding (logo and colours).

**Done when:** two realms display different branding from the same theme.

## M1.6 — Scale Test and Go/No-Go (Gate G1)

**Depends on:** M1.4, M1.5

- [ ] Provision approximately 500 realms and measure provisioning time, memory and login latency.
- [ ] Define the realm-count threshold for sharding to a new cluster.
- [ ] Verify licences for Keycloak, Keycloakify and major dependencies.
- [ ] Write the validation report.
- [ ] Go/no-go decision on ADR-001 recorded.

**Done when:** the decision is signed off. On "no-go", evaluate the OpenIddict fallback before Phase 2 begins.

---

# 4. Phase 2 — Core Identity Platform (Weeks 6–11)

## M2.1 — Secure Realm Template

**Depends on:** M1.6

- [ ] Version-controlled realm template in `/identity/keycloak`.
- [ ] Argon2id hashing with documented parameters.
- [ ] Password policy.
- [ ] Brute-force detection and lockout.
- [ ] Access token lifetime of 10 minutes; refresh rotation enabled.
- [ ] Idle and absolute session timeouts.
- [ ] PKCE required; implicit flow and password grant disabled.
- [ ] Strict redirect URI matching.

**Done when:** a template-applied realm passes an automated configuration check against every default above.

## M2.2 — Email Delivery and Templates

**Depends on:** M2.1

- [ ] Configurable SMTP provider.
- [ ] Templates for verification, recovery and security alerts.
- [ ] Per-tenant template overrides.
- [ ] Per-tenant sending quotas.

**Done when:** each email type is delivered and rendered correctly for two different tenants.

## M2.3 — Signing Keys and Rotation

**Depends on:** M2.1

- [ ] Asymmetric signing (RS256 or ES256).
- [ ] JWKS endpoint published.
- [ ] Scripted key rotation with an overlap period.
- [ ] Emergency rotation procedure documented and tested.

**Done when:** keys rotate without invalidating active sessions, and emergency rotation invalidates all tokens.

## M2.4 — Abuse Protection

**Depends on:** M2.1

- [ ] Edge rate limiting on login, registration, recovery, verification and token endpoints.
- [ ] Account- and IP-based throttling backed by Redis.
- [ ] Enumeration-resistant responses for registration and recovery.

**Done when:** automated tests confirm throttling triggers and responses do not reveal whether an account exists.

## M2.5 — Audit Event Pipeline

**Depends on:** M2.1

- [ ] Keycloak event listener forwarding authentication events.
- [ ] Events stored in the Dovepeak audit store with tenant identifiers.
- [ ] Structured logging with credentials and tokens redacted.

**Done when:** login, logout, failure and credential-change events appear in the audit store, and a log scan finds no secrets.

## M2.6 — Identity Lifecycle Test Suite

**Depends on:** M2.2, M2.3, M2.4, M2.5

- [ ] Automated tests for registration, login, verification, recovery, refresh, reuse detection, logout, session expiry and session revocation.
- [ ] Tests run in CI against a disposable Keycloak instance.

**Done when:** the full suite passes in CI on every pull request.

---

# 5. Phase 3 — Management API and Multi-Tenancy (Weeks 12–18)

## M3.1 — Management API Foundation

**Depends on:** M2.6

- [ ] ASP.NET Core modular monolith structure.
- [ ] `/v1` versioned routing.
- [ ] Standard error format (RFC 9457 Problem Details).
- [ ] OpenAPI generation.
- [ ] EF Core with migrations.
- [ ] Developer authentication through the Keycloak "platform" realm.

**Done when:** an authenticated developer can call a `/v1` endpoint and the OpenAPI document is generated in CI.

## M3.2 — Tenant Authorization Framework

**Depends on:** M3.1

- [ ] Central authorization policy checking organization, project, application and environment access.
- [ ] EF Core global query filters on tenant identifiers.
- [ ] PostgreSQL Row-Level Security policies.
- [ ] 404 responses for resources in other tenants.
- [ ] Tenant-isolation test harness added to CI.

**Done when:** the harness runs in CI and a deliberately unprotected test endpoint fails it.

## M3.3 — Organizations and Membership

**Depends on:** M3.2

- [ ] Organization CRUD.
- [ ] Member invitations.
- [ ] Roles: owner, admin, developer, viewer.
- [ ] Isolation tests for every endpoint.

**Done when:** members can only perform actions allowed by their role, and isolation tests pass.

## M3.4 — Projects and Environments

**Depends on:** M3.3

- [ ] Project CRUD.
- [ ] Development, staging and production environments per project.
- [ ] Each environment provisions its own realm from the M2.1 template.
- [ ] `tenant → keycloak_cluster` mapping stored.
- [ ] Isolation tests for every endpoint.

**Done when:** creating a project provisions three isolated realms, and deleting it cleans them up.

## M3.5 — Applications and Client Configuration

**Depends on:** M3.4

- [ ] Application types: SPA, native, server-side web, machine-to-machine.
- [ ] Callback URLs, logout URLs and CORS origins with validation.
- [ ] Authentication methods, token and session policies.
- [ ] Public configuration endpoint containing no secrets.
- [ ] Isolation tests for every endpoint.

**Done when:** application settings are reflected in Keycloak, and the public configuration endpoint is verified to contain no secrets.

## M3.6 — Credentials Management

**Depends on:** M3.5

- [ ] Confidential client secrets shown once.
- [ ] Developer API keys with prefixes, stored as keyed digests.
- [ ] Rotation with overlap window.
- [ ] Expiration and immediate revocation.
- [ ] Scopes and quotas on API keys.

**Done when:** no endpoint can return a secret after creation, and revoked credentials are rejected immediately.

## M3.7 — Configuration Reconciliation

**Depends on:** M3.5

- [ ] Outbox pattern for Keycloak changes.
- [ ] Idempotent provisioning operations.
- [ ] Reconciliation worker detecting and fixing drift.
- [ ] Drift alerts.

**Done when:** a manual change made directly in Keycloak is detected and reverted automatically.

## M3.8 — Roles, Scopes and Authorization Data

**Depends on:** M3.5

- [ ] Application-specific roles.
- [ ] Role assignment to users.
- [ ] OAuth scopes and resource audiences.
- [ ] Roles and scopes included in issued tokens.

**Done when:** a protected API can enforce a role and a scope from a token issued for a configured application.

## M3.9 — Audit Log, Webhooks and Quotas (Gate G2 — Alpha)

**Depends on:** M3.6, M3.7, M3.8

- [ ] Append-only administrative audit log.
- [ ] Webhooks with HMAC signatures, retries and exponential backoff.
- [ ] Idempotency keys on create operations.
- [ ] Per-tenant quotas.
- [ ] Isolation suite covers 100% of Management API endpoints.

**Done when:** all Phase 3 tests pass and the first Dovepeak internal application is onboarded (Gate G2).

---

# 6. Phase 4 — Developer Portal (Weeks 15–21)

## M4.1 — Portal Shell and Authentication

**Depends on:** M3.1

- [ ] Next.js with TypeScript.
- [ ] Design tokens and components from the Dovepeak brand guidelines.
- [ ] Login through the "platform" realm.
- [ ] Layout, navigation and error handling.

**Done when:** a developer can sign in and sign out of the portal.

## M4.2 — Organization and Project Management

**Depends on:** M4.1, M3.4

- [ ] Organization settings and member management.
- [ ] Project and environment creation and listing.

**Done when:** a developer can create an organization, invite a member and create a project from the portal.

## M4.3 — Application Settings

**Depends on:** M4.2, M3.5

- [ ] Application creation by type.
- [ ] Callback URLs, logout URLs and allowed origins.
- [ ] Authentication methods, token and session policies.
- [ ] Branding and hosted login page settings.
- [ ] Email template editor.

**Done when:** every application setting in the Management API can be changed from the portal.

## M4.4 — Credentials and Sessions

**Depends on:** M4.3, M3.6

- [ ] Create, rotate and revoke credentials, with secrets shown once.
- [ ] Active session listing and revocation.

**Done when:** a revoked credential or session stops working, and secrets cannot be viewed again.

## M4.5 — Dashboard, Audit Log and Webhooks

**Depends on:** M4.4, M3.9

- [ ] Dashboard with authentication activity, failures and security events.
- [ ] Filterable audit log.
- [ ] Webhook configuration and delivery history.

**Done when:** events from a test application appear on the dashboard and in the audit log.

## M4.6 — Integration Guide and Documentation Site

**Depends on:** M4.5

- [ ] Per-application integration tab with pre-filled client ID and URLs.
- [ ] Documentation site (Docusaurus or Nextra).
- [ ] Getting-started and framework guides.
- [ ] API reference generated from OpenAPI.
- [ ] Error reference and troubleshooting.

**Done when:** a developer new to the platform reaches a working login using only the portal and documentation.

---

# 7. Phase 5 — SDKs and Developer Experience (Weeks 19–24)

## M5.1 — TypeScript SDK Core

**Depends on:** M3.5

- [ ] `@dovepeak/identity` package built on `oauth4webapi` and `jose`.
- [ ] Separate browser-safe and confidential server configuration types.
- [ ] Typed error classes.
- [ ] Unit tests.

**Done when:** the core client completes Authorization Code with PKCE against a live realm in CI.

## M5.2 — React and Next.js Adapters

**Depends on:** M5.1

- [ ] `@dovepeak/identity/react` provider and hooks.
- [ ] `@dovepeak/identity/next` BFF route handlers and middleware.
- [ ] Documented browser threat model and token storage behaviour.

**Done when:** a Next.js app adds login, logout and a protected page using only the adapters.

## M5.3 — Node.js Token Verification

**Depends on:** M5.1

- [ ] `@dovepeak/identity/node` verification helper.
- [ ] Validates issuer, audience, signature, expiry and claims.
- [ ] Optional introspection mode.

**Done when:** verification tests reject every invalid token case from M1.3.

## M5.4 — .NET SDK

**Depends on:** M3.8

- [ ] `Dovepeak.Identity` NuGet package.
- [ ] One-line JWT bearer setup for ASP.NET Core.
- [ ] Typed Management API client.
- [ ] `IOptions` and `HttpClientFactory` integration.
- [ ] Optional introspection mode.

**Done when:** a .NET API enforces roles and scopes using only the SDK.

## M5.5 — Example Applications

**Depends on:** M5.2, M5.3, M5.4

- [ ] Next.js application using the BFF pattern.
- [ ] React single-page application.
- [ ] .NET protected API.

**Done when:** each example integrates in under 30 minutes by following the documentation.

## M5.6 — SDK Release Pipeline (Gate G3 — Beta)

**Depends on:** M5.5, M4.6

- [ ] Semantic versioning and changesets.
- [ ] Automated publishing to npm and NuGet.
- [ ] Contract tests against the supported Keycloak version.

**Done when:** SDK beta versions are published and the first external beta developers are onboarded (Gate G3).

---

# 8. Phase 6 — Production Readiness (Weeks 25–30)

## M6.1 — Observability

**Depends on:** M5.6

- [ ] OpenTelemetry tracing across portal, API, workers and Keycloak.
- [ ] Prometheus metrics and Grafana dashboards.
- [ ] Alerts for authentication anomalies, latency, email failures and drift.
- [ ] Service-level objectives defined and published.

**Done when:** a simulated incident triggers the correct alert and is traceable end to end.

## M6.2 — Backup, Recovery and High Availability

**Depends on:** M6.1

- [ ] PostgreSQL point-in-time recovery (pgBackRest or WAL-G).
- [ ] Keycloak clustered with at least two nodes.
- [ ] Timeouts, bounded retries, connection pool limits and graceful shutdown.
- [ ] First restore drill completed and recorded.

**Done when:** a restore drill meets the RPO and RTO targets.

## M6.3 — Operational Runbooks

**Depends on:** M6.2

- [ ] Database failure and restoration.
- [ ] Email provider outage.
- [ ] Signing key compromise.
- [ ] Keycloak version upgrade.
- [ ] Tenant sharding to a new Keycloak cluster.

**Done when:** each runbook is rehearsed once by an engineer who did not write it.

## M6.4 — Security Assurance

**Depends on:** M6.2

- [ ] Threat model review and update.
- [ ] Load testing with k6.
- [ ] API fuzzing with Schemathesis.
- [ ] Key rotation and credential revocation tests.
- [ ] Independent penetration test.
- [ ] All critical and high findings fixed.

**Done when:** the penetration test report has no open critical or high findings.

## M6.5 — Production Deployment and Self-Hosting Guide

**Depends on:** M6.3, M6.4

- [ ] Production Docker Compose with Caddy and TLS.
- [ ] Self-hosting guide for a Linux VPS.
- [ ] Production environment deployed.

**Done when:** a clean VPS is deployed from the guide alone.

## M6.6 — Version 1.0 Release (Gate G4)

**Depends on:** M6.5

- [ ] All MVP acceptance criteria verified (see [implementation-phases.md](implementation-phases.md), section 10).
- [ ] Release notes and versioning policy published.
- [ ] Vulnerability disclosure and patch process published.
- [ ] Security limitations and supported flows documented.
- [ ] Public repository and documentation launched.

**Done when:** v1.0 is tagged and published (Gate G4).

---

# 9. Tracking and Governance

## 9.1 Tracking

* Each milestone is a GitHub milestone named by its identifier (for example `M3.4 — Projects and Environments`).
* Each checklist item becomes one or more GitHub issues linked to its milestone.
* A GitHub Project board tracks status: Backlog, In Progress, In Review, Done.

## 9.2 Cadence

| Activity                | Frequency      | Purpose                                         |
| ----------------------- | -------------- | ----------------------------------------------- |
| Sprint planning         | Every 2 weeks  | Select issues from the current milestones       |
| Milestone review        | On completion  | Confirm "done when" conditions are met          |
| Security review         | Every 2 weeks  | Review security-relevant changes and findings   |
| Gate review             | At each gate   | Decide whether to proceed to the next stage     |
| Plan re-baseline        | After G1       | Adjust estimates using Phase 1 measurements     |

## 9.3 Milestone Completion Rules

A milestone is complete only when:

* All checklist items are done.
* The "done when" condition has been demonstrated.
* All related tests pass in CI.
* Documentation is updated.
* No open critical or high security issues relate to it.

## 9.4 Change Control

* Scope added during a phase is placed in the backlog unless it blocks a "done when" condition.
* Changes to ADRs require a new or superseding ADR, not an edit to the original.
* Items deferred after the MVP (passkeys, social login, enterprise SSO, custom domains, analytics, managed hosting) are tracked in a separate post-v1.0 backlog.
