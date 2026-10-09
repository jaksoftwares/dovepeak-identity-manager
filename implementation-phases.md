# Dovepeak Identity — Implementation Phases

**Document Type:** Implementation Plan
**Companion to:** [problem-statement.md](problem-statement.md)
**Status:** Draft for team review

This document turns the product specification into an ordered build plan. Each phase lists its objective, tasks, deliverables and exit criteria. A phase is complete only when its exit criteria are met.

Duration estimates assume a team of 3–5 engineers and should be adjusted to actual team capacity.

---

# 1. Foundational Decisions

These decisions affect every later phase. Each must be recorded as an Architecture Decision Record (ADR) in `/docs/adr/` before Phase 2 begins.

## ADR-001: Identity Engine Selection

The specification requires both **multi-tenancy** and a **headless** developer experience. The candidate engines satisfy these differently:

| Requirement                                   | Ory Kratos + Hydra                         | Keycloak                                         | Logto (OSS)                     |
| --------------------------------------------- | ------------------------------------------ | ------------------------------------------------ | ------------------------------- |
| Multi-tenant (orgs, projects, environments)   | Single-tenant per instance in open source  | Realms provide isolated users, clients and keys  | Multi-tenancy is cloud-only     |
| Headless custom UI                            | Native design goal                         | Themeable login pages (React via Keycloakify)    | Partial                         |
| OAuth 2.0 / OIDC, refresh rotation, MFA, passkeys | Yes (two services to operate)          | Yes (single integrated product)                  | Yes                             |
| Licence                                       | Apache-2.0                                 | Apache-2.0                                       | MPL-2.0                         |

**Recommendation:** Keycloak, with **one realm per project environment** (for example `acme-shop-prod`).

**Definition of "headless" for Dovepeak Identity:**

* Credential entry (login, registration, recovery) uses standards-based redirect flows — Authorization Code with PKCE — to fully brandable hosted pages built with Keycloakify.
* All other capabilities (profile, sessions, roles, user administration, configuration) are fully available through the API and SDKs.
* The Resource Owner Password Credentials grant is not supported. It is deprecated in OAuth 2.1 and contradicts the specification's standards-based principle.

**Known constraints and mitigations:**

* Keycloak performance degrades with very large realm counts per cluster. The platform stores a `tenant → keycloak_cluster` mapping from the start so tenants can be sharded across clusters.
* Keycloak URLs, realm names and other engine-specific concepts must never appear in the public developer contract.

**Fallback:** If Phase 1 validation fails, evaluate OpenIddict with ASP.NET Core Identity. These are mature libraries rather than custom cryptography, but they increase the security surface owned by the team.

## ADR-002: Identity Ownership and Sharing Model

* An identity belongs to exactly one **project environment**. A user in `shop-prod` does not exist in `shop-dev` or in any other project.
* Applications within the same project environment may share that project's user directory (opt-in per application). This provides single sign-on across an organization's related applications.
* Cross-project and cross-organization federation is explicitly out of MVP scope.

## ADR-003: Token Lifetime and Revocation Semantics

* **Access tokens:** Signed JWTs with a default lifetime of 10 minutes, validated locally by resource servers. A revoked session's access token may remain valid until expiry (maximum 10 minutes). This is documented publicly.
* **Refresh tokens:** Rotated on every use. Reuse of a rotated token revokes the entire token family and its session.
* **High-sensitivity APIs:** May use token introspection for immediate revocation. The SDK exposes this as a configuration option.
* **Session timeouts:** Idle and absolute timeouts are configurable per application, with conservative defaults.

## ADR-004: Credential Types

The following credential types are distinct and must never be interchangeable:

| Credential                  | Holder                | Storage                                | Visibility                  |
| --------------------------- | --------------------- | -------------------------------------- | --------------------------- |
| Public client identifier    | SPA, mobile app       | Plain                                  | Public                      |
| Confidential client secret  | Backend application   | Hashed / engine-managed                | Shown once at creation      |
| Developer API key           | Developer automation  | Keyed digest (HMAC)                    | Shown once at creation      |
| User access / refresh token | End-user session      | Engine-managed                         | Never shown in the portal   |

Developer API keys use identifiable prefixes (for example `dpk_live_…`, `dpk_test_…`) so that secret scanners can detect leaks.

---

# 2. Target Architecture

```text
                    ┌───────────────────────────┐
 Developers ───────▶│ Developer Portal (Next.js) │
                    └─────────────┬─────────────┘
                                  │  Portal login via the Keycloak "platform" realm
                    ┌─────────────▼─────────────┐
                    │ Management API (ASP.NET)   │  Modular monolith
                    │  Orgs · Projects · Apps    │──▶ PostgreSQL (dovepeak database)
                    │  Envs · Credentials · Audit│──▶ Redis (rate limits, cache)
                    │  Webhooks · Quotas         │──▶ Background workers
                    └─────────────┬─────────────┘
                                  │  Keycloak Admin API (private network only)
 Client apps ──SDK──▶ ┌───────────▼────────────┐
 (SPA / mobile /      │ Keycloak cluster(s)     │──▶ PostgreSQL (keycloak database)
  BFF / API)  OIDC ──▶│ Realm per project-env   │
                      └─────────────────────────┘

 Edge: Caddy or Traefik (TLS, routing, edge rate limiting)
 Observability: OpenTelemetry → Prometheus / Grafana
```

## 2.1 Ownership Boundaries

| Data                                                         | Owner           |
| ------------------------------------------------------------ | --------------- |
| Identities, credentials, sessions, tokens, signing keys      | Keycloak        |
| Organizations, projects, environments, applications          | Management API  |
| Developer API keys, quotas, webhooks                         | Management API  |
| Administrative and security audit trail                      | Management API  |

The Management API stores the **desired configuration** and reconciles it into Keycloak. Keycloak remains the source of truth for identity state. No security-critical state is duplicated.

## 2.2 Repository Layout

```text
/docs                       ADRs, architecture, threat model, runbooks
/services/management-api    ASP.NET Core, modular monolith
/services/workers           .NET hosted workers (email, events, reconciliation, cleanup)
/identity/keycloak          Realm templates, Keycloakify theme, custom SPIs
/portal                     Next.js + TypeScript developer portal
/sdks/typescript            @dovepeak/identity (+ react, next, node adapters)
/sdks/dotnet                Dovepeak.Identity
/examples                   Sample Next.js app, React SPA, .NET protected API
/deploy                     Docker Compose (dev and production), Caddy configuration
/tests                      End-to-end, tenant isolation, load tests
/branding                   Dovepeak brand assets and guidelines
```

---

# 3. Phase 0 — Project Foundations

**Duration:** 1–2 weeks
**Objective:** Establish the repository, engineering standards and local development environment.

## Tasks

* Initialise the monorepo using the layout in section 2.2.
* Add the open-source governance files:
  * `LICENSE` (Apache-2.0, consistent with Keycloak).
  * `CONTRIBUTING.md`.
  * `CODE_OF_CONDUCT.md`.
  * `SECURITY.md` with vulnerability disclosure instructions.
* Configure GitHub Actions CI:
  * Build, lint and unit tests for every project.
  * CodeQL static analysis.
  * Trivy container and dependency scanning.
  * Gitleaks secret scanning.
  * Dependabot dependency updates.
* Create the local development stack with Docker Compose:
  * PostgreSQL.
  * Redis.
  * Keycloak.
  * Mailpit (local email capture).
  * Management API skeleton with health checks.
* Write ADR-001 to ADR-004.
* Produce an initial threat model (STRIDE) identifying trust boundaries between end users, client applications, the portal, the Management API and Keycloak.
* Define branching strategy, pull request template and code review rules.

## Deliverables

* Public-ready repository with governance files.
* `docker compose up` starts the full local stack.
* Approved ADRs and initial threat model.

## Exit Criteria

* A new engineer can clone the repository and run the stack using only the README.
* CI runs on every pull request and blocks merging on failures.

---

# 4. Phase 1 — Architecture and Technical Validation

**Duration:** 2–3 weeks
**Objective:** Prove the selected architecture before building the platform.

## Tasks

* Programmatically create a realm from a prototype Management API endpoint.
* Register a public SPA client and a confidential backend client through the Keycloak Admin API.
* Build a throwaway Next.js application using Authorization Code with PKCE through a backend-for-frontend (BFF):
  * Registration.
  * Login and logout.
  * Email verification.
  * Password recovery.
* Build a .NET protected API that validates issuer, audience, signature, expiry and required claims.
* Demonstrate refresh token rotation and reuse detection.
* Build a branded login page with Keycloakify using the Dovepeak brand guidelines.
* Load-test realm provisioning and login with approximately 500 realms to measure realm-count limits.
* Verify current licences for Keycloak, Keycloakify and all major dependencies.
* Document security assumptions and threat boundaries.

## Deliverables

* Working authentication proof of concept.
* Validation report with measured results.
* Final go/no-go decision on ADR-001.

## Exit Criteria

* All flows above work end to end.
* Realm-scaling results are recorded and a sharding threshold is defined.
* If any criterion fails, the fallback in ADR-001 is evaluated before proceeding.

---

# 5. Phase 2 — Core Identity Platform

**Duration:** 4–6 weeks
**Objective:** Establish reliable, secure identity lifecycle operations.

## Tasks

### Realm templates

Create version-controlled realm templates with secure defaults:

* Argon2id password hashing with documented parameters.
* Password policy (length, breached-password checks where supported).
* Brute-force detection and temporary lockout.
* Access token lifetime of 10 minutes.
* Refresh token rotation with reuse detection.
* Idle and absolute session timeouts.
* PKCE required for all public clients.
* Implicit flow and password grant disabled.
* Strict redirect URI matching (no wildcards in production).

### Email

* SMTP integration with a configurable provider.
* Per-tenant email templates for verification, recovery and security alerts.
* Sending quotas per tenant.

### Signing keys

* Asymmetric signing (RS256 or ES256).
* JWKS endpoint for verification keys.
* Scripted key rotation procedure with overlap period.
* Documented emergency rotation procedure for compromised keys.

### Abuse protection

* Edge rate limiting (Caddy and Redis) on login, registration, recovery, verification and token endpoints.
* Account- and IP-based throttling.
* Enumeration-resistant responses for registration and recovery.

### Audit events

* Keycloak event listener forwarding login, logout, failure and credential-change events into the Dovepeak audit pipeline.
* Structured logging with credentials and tokens redacted.

### Testing

* Automated integration tests for registration, login, email verification, password recovery, refresh, reuse detection, logout, session expiry and session revocation.

## Deliverables

* A functioning authentication service usable by a test application.
* Realm templates stored in `/identity/keycloak`.

## Exit Criteria

* All identity lifecycle tests pass in CI.
* No credentials or tokens appear in any log output.

---

# 6. Phase 3 — Management API and Multi-Tenancy

**Duration:** 5–7 weeks
**Objective:** Allow multiple developers and applications to use the platform safely.

## Tasks

### Domain model

* Organizations.
* Organization members with roles: owner, admin, developer, viewer.
* Projects.
* Environments: development, staging, production.
* Applications: SPA, native mobile, server-side web, machine-to-machine.
* Callback URLs, logout URLs and CORS origins.
* Authentication method, token and session policies.
* Application roles and scopes.

### Tenant authorization

* A single authorization layer used by every endpoint, verifying the caller's access to the organization, project, application and environment.
* Tenant identifiers enforced on every query using EF Core global query filters.
* PostgreSQL Row-Level Security as a second layer of defence.
* Responses return 404 for resources in other tenants to avoid disclosing their existence.

### Credentials

* Confidential client secrets and developer API keys displayed once and stored as hashes or keyed digests.
* Prefixed key formats for secret-scanner detection.
* Rotation with a configurable overlap window.
* Immediate revocation.
* Expiration support.

### Configuration reconciliation

* Configuration changes written to PostgreSQL and propagated to Keycloak through an outbox pattern.
* Idempotent provisioning operations.
* A reconciliation worker that detects and corrects drift, with alerts.

### Platform capabilities

* Idempotency keys on create operations.
* Append-only administrative audit log.
* Webhooks with HMAC signatures, retries and exponential backoff.
* Per-tenant quotas.
* OpenAPI specification generated from code under `/v1`.

### Tenant-isolation test suite

* For every endpoint, attempt access with Organization A's credentials to Organization B's resources and assert 403 or 404.
* The suite runs in CI permanently and must be extended with every new endpoint.

## Deliverables

* A versioned Management API with isolated project configuration.
* Published OpenAPI specification.

## Exit Criteria

* Tenant-isolation suite covers 100% of Management API endpoints.
* Configuration drift is detected and corrected automatically.
* Secrets cannot be retrieved after creation through any endpoint.

---

# 7. Phase 4 — Developer Portal

**Duration:** 5–7 weeks (can overlap with Phase 3 once the API contract is stable)
**Objective:** Make the platform accessible to developers without knowledge of its internals.

## Tasks

### Foundation

* Next.js with TypeScript, styled according to the Dovepeak brand guidelines in `/branding`.
* Portal authentication through the Keycloak "platform" realm, so the platform uses its own product.

### Screens

* **Dashboard:** projects, applications, authentication activity, failures, security events and service status.
* **Organization management:** members, invitations and roles.
* **Projects and environments:** creation and configuration.
* **Application settings:**
  * Name, identifier and type.
  * Callback URLs, logout URLs and allowed origins.
  * Authentication methods.
  * Token and session policies.
  * Branding and hosted login page settings.
  * Email templates.
* **Credentials:** creation (shown once), rotation and revocation.
* **Sessions:** active session listing and revocation.
* **Audit log:** filterable security and administrative events.
* **Webhooks:** configuration and delivery history.

### Integration experience

* A per-application "Integration guide" tab that pre-fills the application's real client ID and URLs into copy-paste snippets.
* Documentation site (Docusaurus or Nextra) containing:
  * Getting-started guides.
  * Framework integration guides.
  * API reference generated from the OpenAPI specification.
  * Error reference.
  * Troubleshooting guides.

## Deliverables

* A usable developer portal.
* A public documentation site.

## Exit Criteria

* A developer with no prior knowledge can create a project and application and reach a working login using only the portal and documentation.
* The portal never displays a secret after its initial creation.

---

# 8. Phase 5 — SDKs and Developer Experience

**Duration:** 4–6 weeks
**Objective:** Reduce integration effort to a predictable, documented workflow.

## Tasks

### TypeScript SDK — `@dovepeak/identity`

* Core OIDC client built on vetted libraries (`oauth4webapi` for protocol handling, `jose` for JWT verification). No hand-written protocol or cryptographic code.
* Adapters:
  * `@dovepeak/identity/react` — provider and hooks.
  * `@dovepeak/identity/next` — BFF route handlers and middleware.
  * `@dovepeak/identity/node` — server-side token verification.
* Separate types for browser-safe configuration and confidential server configuration, so secrets cannot be placed in browser bundles by mistake.
* Clear, typed error classes.
* Documented browser threat model and token storage behaviour.

### .NET SDK — `Dovepeak.Identity`

* One-line JWT bearer validation setup for ASP.NET Core.
* Typed Management API client.
* `IOptions` configuration and `HttpClientFactory` integration.
* Optional introspection-based validation for high-sensitivity APIs.

### Examples

* Next.js application using the BFF pattern.
* React single-page application.
* .NET protected API.

### Release engineering

* Semantic versioning.
* Changesets and automated release notes.
* Contract tests against a live Keycloak instance in CI.

## Deliverables

* Published TypeScript and .NET SDKs.
* Working example applications.

## Exit Criteria

* Each example application integrates authentication in under 30 minutes by following the documentation.
* SDK contract tests pass against the supported Keycloak version.

---

# 9. Phase 6 — Production Readiness

**Duration:** 4–6 weeks
**Objective:** Deliver a dependable, versioned release with a defensible security posture.

## Tasks

### Observability

* OpenTelemetry tracing across portal, Management API, workers and Keycloak.
* Grafana dashboards for authentication latency, failure rates, email delivery and reconciliation drift.
* Alerting for anomalous authentication activity and service degradation.

### Service-level objectives

Define and publish targets before promising enterprise reliability. Proposed starting points:

| Objective                         | Target       |
| --------------------------------- | ------------ |
| Availability                      | 99.9%        |
| Login latency (p95)               | < 300 ms     |
| Recovery point objective (RPO)    | 15 minutes   |
| Recovery time objective (RTO)     | 1 hour       |

### Resilience

* PostgreSQL point-in-time recovery (pgBackRest or WAL-G).
* **Monthly restore drills**, with results recorded.
* Keycloak clustered with at least two nodes.
* Explicit request timeouts, bounded retries and connection pool limits.
* Graceful shutdown and readiness probes on all services.

### Runbooks

* Database failure and restoration.
* Email provider outage.
* Signing key compromise (emergency rotation and global session revocation).
* Keycloak version upgrade.
* Tenant sharding to an additional Keycloak cluster.

### Security assurance

* Complete threat model review.
* Tenant isolation and authorization testing.
* Load testing with k6.
* API fuzzing with Schemathesis against the OpenAPI specification.
* Key rotation and credential revocation tests.
* **Independent penetration test** before onboarding external customers.
* Published vulnerability disclosure and patch release process.

### Deployment

* Production Docker Compose configuration with Caddy for TLS.
* Documented self-hosting guide for a Linux VPS.
* Kubernetes and Helm charts deferred until there is a demonstrated need.

## Deliverables

* Version 1.0 release with documentation and release notes.

## Exit Criteria

* All acceptance criteria in section 10 are met.

---

# 10. MVP Acceptance Criteria

Mapped from section 16 of the problem statement. The MVP is complete only when all items are verified.

| Criterion                                                          | Verified in |
| ------------------------------------------------------------------ | ----------- |
| A developer can register a project and an application              | Phase 4     |
| A sample application can register and authenticate a user         | Phase 5     |
| Email verification and password recovery work correctly           | Phase 2     |
| Sessions expire and can be revoked according to documented policy | Phase 2     |
| Access tokens are validated correctly by a protected backend       | Phase 5     |
| Refresh token handling meets ADR-003                               | Phase 2     |
| Invalid, expired and revoked credentials are rejected              | Phase 2–3   |
| Applications cannot access another tenant's configuration          | Phase 3     |
| Public client configuration contains no confidential secrets       | Phase 5     |
| Secrets are not exposed in logs, errors or frontend bundles        | Phase 2–5   |
| Rate limiting and abuse protections are operational                | Phase 2     |
| Database backup and restoration have been tested                   | Phase 6     |
| The service runs locally and deploys using documented procedures   | Phase 0, 6  |
| Automated tests cover identity lifecycle and authorization         | Phase 2–3   |
| Security limitations and supported flows are documented honestly   | Phase 4–6   |

---

# 11. Timeline Summary

| Phase | Name                                | Duration   | Can overlap with |
| ----- | ----------------------------------- | ---------- | ---------------- |
| 0     | Project Foundations                 | 1–2 weeks  | —                |
| 1     | Architecture and Technical Validation | 2–3 weeks | —                |
| 2     | Core Identity Platform              | 4–6 weeks  | —                |
| 3     | Management API and Multi-Tenancy    | 5–7 weeks  | Phase 4          |
| 4     | Developer Portal                    | 5–7 weeks  | Phase 3, 5       |
| 5     | SDKs and Developer Experience       | 4–6 weeks  | Phase 4          |
| 6     | Production Readiness                | 4–6 weeks  | —                |

**Estimated total to MVP:** approximately 6–8 months with parallel work on Phases 3–5.

---

# 12. Engineering Practices

## Definition of Done

Every feature is complete only when it includes:

* Unit and integration tests.
* Tenant-isolation tests for any new endpoint.
* Updated documentation.
* Emitted audit events where relevant.
* Confirmation that no secrets appear in logs or errors.
* Threat model notes reviewed for security-relevant changes.

## Security in the Pipeline

* Every pull request runs static analysis, dependency scanning, container scanning and secret scanning.
* Changes to authentication, authorization or credential handling require two reviewers.

## Dogfooding

* Dovepeak's own internal applications adopt Dovepeak Identity from Phase 3 onwards to surface integration issues early.

## Scope Discipline

The following are explicitly deferred until after the MVP:

* Passkeys (WebAuthn).
* Social login providers.
* Enterprise SSO and federation.
* Custom domains and white-label pages.
* Advanced analytics.
* Managed hosting.

Most of these are supported natively by Keycloak and will primarily require configuration and portal work.

---

# 13. Key Risks

| Risk                                      | Mitigation                                                                 |
| ----------------------------------------- | -------------------------------------------------------------------------- |
| Keycloak realm-count scaling limits       | Measured in Phase 1; tenant-to-cluster mapping supports sharding           |
| Keycloak upgrade breaking changes         | Pinned versions; realm templates tested against the next version in CI     |
| Configuration drift (PostgreSQL ↔ Keycloak) | Outbox pattern, reconciliation worker and drift alerts                   |
| Email deliverability                      | Reputable provider, SPF/DKIM/DMARC, delivery monitoring as an SLO          |
| Engine coupling in the public contract    | Public API and SDKs never expose Keycloak-specific concepts                |
| Open-source sustainability costs          | Free core; optional managed hosting and support defined early              |
| Overstated security claims                | Claims limited to tested and documented controls; independent pen test     |
