# ADR-0001: Identity Engine Selection

**Status:** Proposed (pending Phase 1 validation, milestone M1.6)
**Date:** 2026-10-09
**Deciders:** Dovepeak Identity maintainers

## Context

The problem statement (section 6) requires a mature identity engine rather than custom authentication and cryptography. It names Ory Kratos/Hydra, Keycloak and Logto as candidates.

Two requirements constrain the choice:

* **Multi-tenancy** (section 7): many organizations, projects and environments with strict isolation of users, credentials, keys and configuration.
* **Headless operation** (section 3.2): developers must be able to build their own authentication interfaces.

Additional requirements: OAuth 2.0 / OpenID Connect, refresh token rotation with reuse detection, MFA, passkeys (post-MVP), self-hosting, and a licence compatible with an Apache-2.0 open-source project.

## Options Considered

### 1. Ory Kratos + Ory Hydra

* **Advantages:** headless by design; clean APIs; Apache-2.0.
* **Disadvantages:** the open-source edition is single-tenant per deployment. Multi-tenancy is provided by the commercial Ory Network. Achieving tenant isolation would require one Kratos/Hydra deployment per tenant or a custom tenancy layer. Two services must be configured and operated together.

### 2. Keycloak

* **Advantages:** realms provide isolated user directories, clients, signing keys, sessions and policies within one deployment; mature OAuth 2.0/OIDC; refresh rotation, MFA, WebAuthn and identity brokering built in; comprehensive Admin REST API; Apache-2.0; large community.
* **Disadvantages:** login pages are server-rendered themes rather than headless APIs; performance degrades with very large realm counts per cluster; Java runtime with higher memory footprint.

### 3. Logto (open source)

* **Advantages:** developer-friendly; modern UI.
* **Disadvantages:** multi-tenancy is a cloud-only capability; MPL-2.0 licence.

### 4. OpenIddict + ASP.NET Core Identity (fallback)

* **Advantages:** native to the .NET stack; full control of tenancy model; Apache-2.0.
* **Disadvantages:** these are libraries, not a product. The team owns significantly more security-critical code (account lifecycle, MFA, session management, admin tooling).

## Decision

Adopt **Keycloak** as the identity engine, using **one realm per project environment** (for example `acme-shop-prod`).

"Headless" is defined for Dovepeak Identity as follows:

* Credential entry (login, registration, recovery, MFA challenges) uses standards-based redirect flows (Authorization Code with PKCE) to hosted pages that tenants can fully brand. Pages are built with Keycloakify (React).
* All other capabilities (profile, sessions, roles, user administration, configuration) are fully available through the Dovepeak API and SDKs.
* The Resource Owner Password Credentials grant is not supported. It is deprecated by OAuth 2.1 and would require client applications to handle user passwords directly.

Keycloak is an **internal implementation component**. Keycloak URLs, realm names and Keycloak-specific concepts must not appear in the public API or SDK contracts (problem statement section 17).

## Consequences

* Tenant isolation is enforced by the engine at the realm boundary, in addition to Dovepeak's own authorization layer.
* The Management API must store a `tenant → keycloak_cluster` mapping from the first release so that tenants can be distributed across multiple Keycloak clusters when realm counts grow.
* Hosted login pages require a theme build pipeline (Keycloakify) and per-tenant branding configuration.
* Keycloak upgrades must be tested against realm templates in CI before adoption.

## Validation

Phase 1 milestones M1.1–M1.6 must demonstrate:

* Programmatic realm and client provisioning.
* End-to-end Authorization Code with PKCE through a BFF.
* Correct token validation by a .NET resource server.
* Refresh token rotation with reuse detection.
* Per-tenant branding from a single theme.
* Measured behaviour at approximately 500 realms, producing a documented sharding threshold.

If validation fails, Option 4 (OpenIddict) is evaluated before Phase 2 begins, and this ADR is superseded.
