# ADR-0006: Hosted Login Theming Approach

**Status:** Proposed
**Date:** 2026-10-09
**Deciders:** Dovepeak Identity maintainers

## Context

ADR-0001 defines credential entry as hosted, brandable pages and named Keycloakify (React) as the theming tool. Phase 1 (M1.5) had to prove that one theme can serve every tenant while showing tenant-specific branding.

Keycloakify builds themes as Java archives and requires Maven in the build toolchain. Its main benefit — a React component model shared with the developer portal — only materialises once the portal's design system exists (Phase 4).

## Options Considered

1. **Keycloakify now** — full control of markup in React; adds a Maven + Node build pipeline and a theme JAR to maintain before any UI design system exists.
2. **CSS-only child theme of `keycloak.v2`** — no copied templates, no build step; Keycloak upgrades do not require merging forked FreeMarker files. Limited to what CSS and realm settings can change.
3. **Forked FreeMarker templates** — full markup control without a new toolchain, but every Keycloak upgrade requires manual merges.

## Decision

* **Phase 1–3:** use a **CSS-only child theme** (`identity/keycloak/themes/dovepeak`) of `keycloak.v2`, applied to every realm by the realm template.
  * Platform branding (palette, typography, "Secured by Dovepeak Identity") is in the theme.
  * The tenant's name is shown in the page header from the realm display name. Provisioning always HTML-encodes it.
* **Phase 4:** re-evaluate Keycloakify together with the developer portal's design system, for per-tenant logos, colours and layout.

## Consequences

* Keycloak upgrades are low-risk for the login UI; form IDs and field names are unchanged, so the integration test harness keeps working.
* Per-tenant visual customisation beyond the display name (logo, colours) is not available until Phase 4.
* ADR-0001's mention of Keycloakify becomes a Phase 4 decision rather than a Phase 1 commitment.

## Validation

`HostedLoginThemeTests` (M1.5):

* Login pages load the Dovepeak stylesheet.
* Two tenants share the theme but each shows only its own name.
* A display name containing `<script>` is rendered as text, never as markup.
